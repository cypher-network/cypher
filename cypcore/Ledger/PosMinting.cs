// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;

using Dawn;
using libsignal.ecc;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Serilog;

using CYPCore.Extentions;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Network;

namespace CYPCore.Ledger
{
    public class PosMinting : IPosMinting
    {
        private const string KeyName = "PosMintingProvider.Key";

        private readonly ISerfClient _serfClient;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signing;
        private readonly IValidator _validator;
        private readonly ILocalNode _localNode;
        private readonly StakingConfigurationOptions _stakingConfigurationOptions;
        private readonly ILogger _logger;
        private readonly KeyPair _keyPair;

        public PosMinting(ISerfClient serfClient, IUnitOfWork unitOfWork,
            ISigning signing, IValidator validator, ILocalNode localNode,
            StakingConfigurationOptions stakingConfigurationOptions, ILogger logger)
        {
            _serfClient = serfClient;
            _unitOfWork = unitOfWork;
            _signing = signing;
            _validator = validator;
            _localNode = localNode;
            _stakingConfigurationOptions = stakingConfigurationOptions;
            _logger = logger.ForContext("SourceContext", nameof(PosMinting));

            _keyPair = _signing.GetOrUpsertKeyName(KeyName).ConfigureAwait(false).GetAwaiter().GetResult();
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
            var lastBlockHeader = await _unitOfWork.DeliveredRepository.LastAsync();
            if (lastBlockHeader == null)
            {
                _logger.Here().Information("There is no block header for processing");
                return;
            }

            var coinstakeTimestamp = _validator.GetAdjustedTimeAsUnixTimestamp();
            if (coinstakeTimestamp <= lastBlockHeader.Locktime)
            {
                _logger.Here().Warning("Current coinstake time {@Timestamp} is not greater than last search timestamp {@Locktime}",
                    coinstakeTimestamp,
                    lastBlockHeader.Locktime);

                return;
            }

            var transactions = (await IncludeTransactions()).ToList();
            if (transactions.Any() != true)
            {
                _logger.Here().Warning("Cannot add zero transactions to the block header");
                return;
            }

            await _validator.GetRunningDistribution();

            uint256 hash;
            using (TangramStream ts = new())
            {
                transactions.ForEach(x => { ts.Append(x.Stream()); });
                hash = NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray());
            }
            
            var signature = _signing.CalculateVrfSignature(Curve.decodePrivatePoint(_keyPair.PrivateKey), hash.ToBytes(false));
            var vrfSig = _signing.VerifyVrfSignature(Curve.decodePoint(_keyPair.PublicKey, 0), hash.ToBytes(false), signature);
            var solution = _validator.Solution(vrfSig, hash.ToBytes(false));
            var networkShare = _validator.NetworkShare(solution);
            var reward = _validator.Reward(solution);
            var bits = _validator.Difficulty(solution, networkShare);

            try
            {
                var coinstakeTransaction = await CreateCoinstakeTransaction(bits, reward);
                if (coinstakeTransaction == null)
                {
                    _logger.Here().Error("Could not create coin stake transaction");
                    return;
                }

                transactions.Insert(0, coinstakeTransaction);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to insert the coinstake transaction");
                return;
            }

            var lastSeenBlockHeader = await _unitOfWork.SeenBlockHeaderRepository.LastAsync();
            var deliveredBlockHeader = await TryGetDeliveredBlockHeader(lastSeenBlockHeader);
            var blockHeader = CreateBlockHeader(transactions.ToArray(), signature, vrfSig, solution, bits, deliveredBlockHeader);

            blockHeader = _unitOfWork.DeliveredRepository.ToTrie(blockHeader);
            if (blockHeader == null)
            {
                _logger.Here().Fatal("Unable to add the block header to merkel");
                return;
            }

            var savedLastSeen = await SaveLastSeenBlockHeader(lastSeenBlockHeader, blockHeader);
            if (!savedLastSeen)
            {
                _logger.Here().Fatal("Unable to save the Last seen block header");
                return;
            }

            var savedBlockHeader = await _unitOfWork.DeliveredRepository.PutAsync(blockHeader.ToIdentifier(), blockHeader);
            if (!savedBlockHeader)
            {
                _logger.Here().Fatal("Unable to save the block header");
                return;
            }

            var published = await PublishBlockHeader(blockHeader);
            if (published)
            {
                lastSeenBlockHeader.Published = true;
                var saved = await _unitOfWork.SeenBlockHeaderRepository.PutAsync(lastSeenBlockHeader.ToIdentifier(), lastSeenBlockHeader);
                if (!saved)
                {
                    _logger.Here().Warning("Unable to update the last seen block header");
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
                ? await _unitOfWork.DeliveredRepository.FirstAsync(x => new ValueTask<bool>(x.MrklRoot == lastSeenBlockHeader.MrklRoot))
                : await _unitOfWork.DeliveredRepository.FirstAsync();

            return deliveredBlockHeader;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactions"></param>
        /// <param name="signature"></param>
        /// <param name="vrfBytes"></param>
        /// <param name="solution"></param>
        /// <param name="bits"></param>
        /// <param name="deliveredBlockHeader"></param>
        /// <returns></returns>
        private BlockHeaderProto CreateBlockHeader(TransactionProto[] transactions, byte[] signature, byte[] vrfBytes, ulong solution, int bits, BlockHeaderProto deliveredBlockHeader)
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
            var data = Util.SerializeProto(blockHeader);
            var payload = new PayloadProto
            {
                Error = false,
                Message = string.Empty,
                Node = _serfClient.ClientId,
                Data = data,
                PublicKey = await _signing.GetPublicKey(_signing.DefaultSigningKeyName),
                Signature = await _signing.Sign(_signing.DefaultSigningKeyName, Util.SHA384ManagedHash(data))
            };

            await _localNode.Broadcast(Util.SerializeProto(payload), TopicType.AddBlock);

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
            currentBlock ??= new SeenBlockHeaderProto();

            currentBlock.MrklRoot = blockHeader.MrklRoot;
            currentBlock.PrevBlock = blockHeader.PrevMrklRoot;

            var saved = await _unitOfWork.SeenBlockHeaderRepository.PutAsync(currentBlock.ToIdentifier(), currentBlock);
            return saved;
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

                    var deleted = await _unitOfWork.InterpretedRepository.RemoveAsync(interpreted.ToIdentifier());
                    if (!deleted)
                    {
                        _logger.Here().Error("Unable to delete interpreted for {@Node} and round {@Round}",
                            interpreted.Node,
                            interpreted.Round);
                    }

                    interpreted.InterpretedType = InterpretedType.Processed;

                    var saved = await _unitOfWork.InterpretedRepository.PutAsync(interpreted.ToIdentifier(), interpreted);
                    if (!saved)
                    {
                        _logger.Here().Error("Unable to save interpreted for {@Node} and round {@Round}",
                            interpreted.Node,
                            interpreted.Round);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Warning(ex, "Cannot include transaction");
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
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-protobuf"));

                try
                {
                    var pub = await _signing.GetPublicKey(_signing.DefaultSigningKeyName);
                    var sendPayment = new SendPaymentProto
                    {
                        Address = _stakingConfigurationOptions.WalletSettings.Address,
                        Amount = ((double)bits).ConvertToUInt64(),
                        Credentials = new CredentialsProto { Identifier = _stakingConfigurationOptions.WalletSettings.Identifier, Passphrase = _stakingConfigurationOptions.WalletSettings.Passphrase },
                        Fee = reward,
                        Memo = $"Coinstake {_serfClient.SerfConfigurationOptions.NodeName}: {pub.ByteToHex()}",
                        SessionType = SessionType.Coinstake
                    };

                    var proto = Util.SerializeProto(sendPayment);

                    var byteArrayContent = new ByteArrayContent(proto);
                    byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

                    using var response = await client.PostAsync(_stakingConfigurationOptions.WalletSettings.SendPaymentEndpoint, byteArrayContent, new System.Threading.CancellationToken());

                    var read = response.Content.ReadAsStringAsync().Result;
                    var jObject = JObject.Parse(read);
                    var jToken = jObject.GetValue("protobuf");
                    var byteArray = Convert.FromBase64String((jToken ?? throw new InvalidOperationException()).Value<string>());

                    if (response.IsSuccessStatusCode)
                        transaction = Util.DeserializeProto<TransactionProto>(byteArray);
                    else
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        _logger.Here().Error("{@Content}\n StatusCode: {@StatusCode}",
                            content,
                            (int)response.StatusCode);

                        throw new Exception(content);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Cannot create coinstake transaction");
                }
            }

            return transaction;
        }
    }
}
