// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;

using Microsoft.Extensions.Logging;

using libsignal.ecc;

using Newtonsoft.Json;

using NBitcoin;

using Dawn;

using CYPCore.Extentions;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using CYPCore.Network.P2P;
using CYPCore.Cryptography;
using Newtonsoft.Json.Linq;

namespace CYPCore.Ledger
{
    public class PosMinting : IPosMinting
    {
        private const string _keyName = "PosMintingProvider.Key";
        private const string _seenBlockHeader = "SeenBlockHeader";

        private readonly ISerfClient _serfClient;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signing;
        private readonly IValidator _validator;
        private readonly ILocalNode _localNode;
        private readonly StakingConfigurationOptions _stakingConfigurationOptions;
        private readonly ILogger _logger;
        private readonly KeyPair _keyPair;
        private readonly IGenericRepository<SeenBlockHeaderProto> _seenBlockHeaderGenericRepository;

        public PosMinting(ISerfClient serfClient, IUnitOfWork unitOfWork,
            ISigning signing, IValidator validator, ILocalNode localNode,
            StakingConfigurationOptions stakingConfigurationOptions, ILogger<PosMinting> logger)
        {
            _serfClient = serfClient;
            _unitOfWork = unitOfWork;
            _signing = signing;
            _validator = validator;
            _localNode = localNode;
            _stakingConfigurationOptions = stakingConfigurationOptions;
            _logger = logger;

            _keyPair = _signing.GetOrUpsertKeyName(_keyName).ConfigureAwait(false).GetAwaiter().GetResult();

            _seenBlockHeaderGenericRepository = _unitOfWork.GenericRepositoryFactory<SeenBlockHeaderProto>();
            _seenBlockHeaderGenericRepository.SetTableType(_seenBlockHeader);

            _validator.SetInitalRunningDistribution(_stakingConfigurationOptions.Distribution);
        }

        /// <summary>
        /// 
        /// </summary>
        public StakingConfigurationOptions StakingConfigurationOptions => _stakingConfigurationOptions;

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task RunStakingBlockAsync()
        {
            var lastBlockHeader = await _unitOfWork.DeliveredRepository.LastOrDefaultAsync();
            if (lastBlockHeader == null)
            {
                _logger.LogInformation($"<<< PosMinting.RunStakingBlockAsync >>>: There is no block header for processing");
                return;
            }

            long coinstakeTimestamp = _validator.GetAdjustedTimeAsUnixTimestamp();
            if (coinstakeTimestamp <= lastBlockHeader.Locktime)
            {
                _logger.LogWarning($"<<< PosMinting.RunStakingBlockAsync >>>: Current coinstake time {coinstakeTimestamp} is not greater than last search timestamp {lastBlockHeader.Locktime}");
                return;
            }

            var transactions = await IncludeTransactions();       

            if (transactions.Any() != true)
            {
                _logger.LogWarning($"<<< PosMinting.RunStakingBlockAsync >>>: Cannot add zero transactions to the block header");
                return;
            }

            uint256 hash;
            using (var ts = new Helper.TangramStream())
            {
                transactions.ForEach(x => ts.Append(x.Stream()));
                hash = NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray());
            }

            var signature = _signing.CalculateVrfSignature(Curve.decodePrivatePoint(_keyPair.PrivateKey), hash.ToBytes(false));
            var vrfSig = _signing.VerifyVrfSignature(Curve.decodePoint(_keyPair.PublicKey, 0), hash.ToBytes(false), signature);


            await _validator.GetRunningDistribution();

            var solution = _validator.Solution(vrfSig, hash.ToBytes(false));
            var networkShare = _validator.NetworkShare(solution);
            var reward = _validator.Reward(solution);
            var bits = _validator.Difficulty(solution, networkShare);

            try
            {
                var coinstakeTxn = await CreateCoinstakeTransaction(bits, reward);
                if (coinstakeTxn == null)
                {
                    _logger.LogError($"<<< PosMinting.RunStakingBlockAsync >>>: Could not create coinstake transaction");
                    return;
                }

                transactions = transactions.TryInsert(0, coinstakeTxn);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< PosMinting.RunStakingBlockAsync >>>: Failed to insert the coinstake transaction: {ex.Message}");
                return;
            }

            var lastSeenBlockHeader = await _seenBlockHeaderGenericRepository.LastOrDefaultAsync();
            var deliveredBlockHeader = await TryGetDeliveredBlockHeader(lastSeenBlockHeader);
            var blockHeader = CreateBlockHeader(transactions, signature, vrfSig, solution, bits, deliveredBlockHeader);

            blockHeader = _unitOfWork.DeliveredRepository.ToTrie(blockHeader);
            if (blockHeader == null)
            {
                _logger.LogCritical($"<<< PosMinting.RunStakingBlockAsync >>>: Unable to add the block header to merkel");
                return;
            }

            var savedLastSeen = await SaveLastSeenBlockHeader(lastSeenBlockHeader, blockHeader);
            if (!savedLastSeen)
            {
                _logger.LogCritical($"<<< PosMinting.RunStakingBlockAsync >>>: Unable to save the Last seen block header");
                return;
            }

            var savedBlockHeader = await _unitOfWork.DeliveredRepository.PutAsync(blockHeader, blockHeader.ToIdentifier());
            if (savedBlockHeader == null)
            {
                _logger.LogCritical($"<<< PosMinting.RunStakingBlockAsync >>>: Unable to save the block header");
                return;
            }

            var published = await PublishBlockHeader(blockHeader);
            if (published)
            {
                lastSeenBlockHeader.Published = true;
                var saved = await _seenBlockHeaderGenericRepository.PutAsync(lastSeenBlockHeader, lastSeenBlockHeader.ToIdentifier());
                if (saved == null)
                {
                    _logger.LogWarning($"<<< PosMinting.RunStakingBlockAsync >>>: Unable to update the last seen block header");
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<BlockHeaderProto> TryGetDeliveredBlockHeader(SeenBlockHeaderProto lastSeenBlockHeader)
        {
            var deliveredBlockHeader = lastSeenBlockHeader != null
                ? await _unitOfWork.DeliveredRepository.FirstOrDefaultAsync(x => x.MrklRoot == lastSeenBlockHeader.MrklRoot)
                : await _unitOfWork.DeliveredRepository.FirstOrDefaultAsync();

            return deliveredBlockHeader;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactions"></param>
        /// <param name="proof"></param>
        /// <param name="vrfBytes"></param>
        /// <param name="solution"></param>
        /// <param name="bits"></param>
        /// <param name="nonce"></param>
        /// <param name="deliveredBlockHeader"></param>
        /// <returns></returns>
        private BlockHeaderProto CreateBlockHeader(IEnumerable<TransactionProto> transactions, byte[] signature, byte[] vrfBytes, ulong solution, int bits, BlockHeaderProto deliveredBlockHeader)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();
            Guard.Argument(signature, nameof(signature)).NotNull().MaxCount(96);
            Guard.Argument(vrfBytes, nameof(vrfBytes)).NotNull().MaxCount(32);
            Guard.Argument(solution, nameof(solution)).NotNegative();
            Guard.Argument(bits, nameof(bits)).NotNegative().NotZero();
            Guard.Argument(deliveredBlockHeader, nameof(deliveredBlockHeader)).NotNull();

            var p256 = System.Numerics.BigInteger.Parse(_validator.Security256.ToStr());
            var x = System.Numerics.BigInteger.Parse(vrfBytes.ByteToHex(), System.Globalization.NumberStyles.AllowHexSpecifier);

            if (x.Sign <= 0)
            {
                x = -x;
            }

            var sloth = new Sloth();
            var nonce = sloth.Eval(bits, x, p256);

            var lockTime = _validator.GetAdjustedTimeAsUnixTimestamp();

            var blockHeader = new BlockHeaderProto
            {
                Bits = bits,
                Height = deliveredBlockHeader.Height + 1,
                Locktime = lockTime,
                LocktimeScript = new Script(Op.GetPushOp(lockTime), OpcodeType.OP_CHECKLOCKTIMEVERIFY).ToString(),
                Nonce = nonce,
                PrevMrklRoot = deliveredBlockHeader.MrklRoot,
                Proof = signature.ByteToHex(),
                Sec = _validator.Security256.ToStr(),
                Seed = _validator.Seed.ByteToHex(),
                Solution = solution,
                Transactions = transactions.ToHashSet(),
                Version = 0x1,
                VrfSig = vrfBytes.ByteToHex(),
            };

            return blockHeader;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        private async Task<bool> PublishBlockHeader(BlockHeaderProto blockHeader)
        {
            var data = Helper.Util.SerializeProto(blockHeader);
            var payload = new PayloadProto
            {
                Error = false,
                Message = string.Empty,
                Node = _serfClient.P2PConnectionOptions.ClientId,
                Payload = data,
                PublicKey = await _signing.GePublicKey(_signing.DefaultSigningKeyName),
                Signature = await _signing.Sign(_signing.DefaultSigningKeyName, Helper.Util.SHA384ManagedHash(data))
            };

            await _localNode.Broadcast(Helper.Util.SerializeProto(payload), SocketTopicType.Block);

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="currentBlock"></param>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        private async Task<bool> SaveLastSeenBlockHeader(SeenBlockHeaderProto currentBlock, BlockHeaderProto blockHeader)
        {
            if (currentBlock == null)
            {
                currentBlock = new SeenBlockHeaderProto();
            }

            currentBlock.MrklRoot = blockHeader.MrklRoot;
            currentBlock.PrevBlock = blockHeader.PrevMrklRoot;

            var saved = await _seenBlockHeaderGenericRepository.PutAsync(currentBlock, currentBlock.ToIdentifier());
            return saved != null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<TransactionProto>> IncludeTransactions()
        {
            var verifiedTransactions = new List<TransactionProto>();

            try
            {
                foreach (var interpreted in (await _unitOfWork.InterpretedRepository.TakeAsync(100)).Where(x => x.InterpretedType == InterpretedType.Pending))
                {
                    var verifiedTx = await _validator.VerifyTransaction(interpreted.Transaction);
                    var verifiedTxFee = _validator.VerifyTransactionFee(interpreted.Transaction);

                    if (!verifiedTx && !verifiedTxFee)
                    {
                        break;
                    }

                    verifiedTransactions.Add(interpreted.Transaction);

                    var deleted = await _unitOfWork.InterpretedRepository.DeleteAsync(new StoreKey { key = interpreted.ToIdentifier(), tableType = _unitOfWork.InterpretedRepository.Table });
                    if (!deleted)
                    {
                        _logger.LogError($"<<< PosMinting.GetNextInterpretedTransactions >>>: Unable to delete interpreted for {interpreted.Node} and round {interpreted.Round}");
                    }

                    interpreted.InterpretedType = InterpretedType.Processed;

                    var saved = await _unitOfWork.InterpretedRepository.PutAsync(interpreted, interpreted.ToIdentifier());
                    if (saved == null)
                    {
                        _logger.LogError($"<<< PosMinting.GetNextInterpretedTransactions >>>: Unable to save interpreted for {interpreted.Node} and round {interpreted.Round}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"<<< PosMinting.GetNextInterpretedTransactions >>>: {ex}");
            }

            return verifiedTransactions;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="reward"></param>
        /// <returns></returns>
        private async Task<TransactionProto> CreateCoinstakeTransaction(int bits, ulong reward)
        {
            Guard.Argument(bits, nameof(bits)).NotNegative();
            Guard.Argument(reward, nameof(reward)).NotNegative();

            TransactionProto transaction = null;

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(_stakingConfigurationOptions.WalletSettings.Url);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                try
                {
                    var pub = await _signing.GePublicKey(_signing.DefaultSigningKeyName);
                    var sendPayment = new SendPaymentProto
                    {
                        Address = _stakingConfigurationOptions.WalletSettings.Address,
                        Amount = ((double)bits).ConvertToUInt64(),
                        Credentials = new CredentialsProto { Identifier = _stakingConfigurationOptions.WalletSettings.Identifier, Passphrase = _stakingConfigurationOptions.WalletSettings.Passphrase },
                        Fee = reward,
                        Memo = $"Coinstake transaction at {DateTime.UtcNow} from {_serfClient.SerfConfigurationOptions.NodeName}: {pub.ByteToHex()}",
                        SessionType = SessionType.Coinstake
                    };

                    var proto = Helper.Util.SerializeProto(sendPayment);

                    using var response = await client.PostAsJsonAsync(_stakingConfigurationOptions.WalletSettings.SendPaymentEndpoint, proto, new System.Threading.CancellationToken());

                    var read = response.Content.ReadAsStringAsync().Result;
                    var jObject = JObject.Parse(read);
                    var jToken = jObject.GetValue("protobuf");
                    var byteArray = Convert.FromBase64String(jToken.Value<string>());
 
                    if (response.IsSuccessStatusCode)
                        transaction = Helper.Util.DeserializeProto<TransactionProto>(byteArray);
                    else
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        _logger.LogError($"<<< PosMinting.CreateCoinstakeTransaction >>>: {content}\n StatusCode: {(int)response.StatusCode}");
                        throw new Exception(content);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"<<< PosMinting.CreateCoinstakeTransaction >>> Message: {ex.Message}\n Stack: {ex.StackTrace}");
                }
            }

            return transaction;
        }
    }
}
