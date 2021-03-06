// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using CYPCore.Consensus.Models;
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
using FlatSharp;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IPosMinting
    {
        StakingConfigurationOptions StakingConfigurationOptions { get; }
        Task RunBlockStakingAsync();
    }

    /// <summary>
    /// 
    /// </summary>
    public class PosMinting : IPosMinting
    {
        private const string KeyName = "PosMintingProvider.Key";

        private readonly IGraph _graph;
        private readonly IMemoryPool _memoryPool;
        private readonly ISerfClient _serfClient;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signing;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly KeyPair _keyPair;

        public PosMinting(IGraph graph, IMemoryPool memoryPool, ISerfClient serfClient,
            IUnitOfWork unitOfWork, ISigning signing, IValidator validator,
            StakingConfigurationOptions stakingConfigurationOptions, ILogger logger)
        {
            _graph = graph;
            _memoryPool = memoryPool;
            _serfClient = serfClient;
            _unitOfWork = unitOfWork;
            _signing = signing;
            _validator = validator;
            StakingConfigurationOptions = stakingConfigurationOptions;
            _logger = logger.ForContext("SourceContext", nameof(PosMinting));

            _keyPair = _signing.GetOrUpsertKeyName(KeyName).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 
        /// </summary>
        public StakingConfigurationOptions StakingConfigurationOptions { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task RunBlockStakingAsync()
        {
            var lastBlockHeader = await _unitOfWork.DeliveredRepository.LastAsync();
            if (lastBlockHeader == null)
            {
                _logger.Here().Information("There is no block header for processing");
                return;
            }

            var coinStakeTimestamp = _validator.GetAdjustedTimeAsUnixTimestamp();
            if (coinStakeTimestamp <= lastBlockHeader.Locktime)
            {
                _logger.Here().Warning(
                    "Current coinstake time {@Timestamp} is not greater than last search timestamp {@Locktime}",
                    coinStakeTimestamp,
                    lastBlockHeader.Locktime);

                return;
            }

            var transactions = await TransactionsAsync().ToListAsync();
            if (transactions.Any() != true)
            {
                _logger.Here().Warning("Cannot add zero transactions to the block header");
                return;
            }

            try
            {
                uint256 hash;
                using (TangramStream ts = new())
                {
                    transactions.ForEach(x => { ts.Append(x.Stream()); });
                    hash = NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray());
                }

                var signature = _signing.CalculateVrfSignature(Curve.decodePrivatePoint(_keyPair.PrivateKey),
                    hash.ToBytes(false));
                var vrfSig = _signing.VerifyVrfSignature(Curve.decodePoint(_keyPair.PublicKey, 0), hash.ToBytes(false),
                    signature);

                var solution = _validator.Solution(vrfSig, hash.ToBytes(false));

                var runningDistribution = await _validator.GetRunningDistribution();
                var networkShare = _validator.NetworkShare(solution, runningDistribution);
                var reward = _validator.Reward(solution, runningDistribution);
                var bits = _validator.Difficulty(solution, networkShare);

                var coinStakeTransaction = await CoinbaseTransactionAsync(bits, reward);
                if (coinStakeTransaction == null)
                {
                    _logger.Here().Error("Could not create coin stake transaction");
                    return;
                }

                transactions.Insert(0, coinStakeTransaction);

                var blockHeader = CreateBlock(transactions.ToArray(), signature, vrfSig, solution, bits, lastBlockHeader);
                blockHeader = _unitOfWork.DeliveredRepository.ToTrie(blockHeader);
                if (blockHeader == null)
                {
                    _logger.Here().Fatal("Unable to add the block header to merkel");
                    return;
                }

                var maxBytesNeeded = FlatBufferSerializer.Default.GetMaxSize(blockHeader);
                var buffer = new byte[maxBytesNeeded];

                FlatBufferSerializer.Default.Serialize(blockHeader, buffer);

                var blockGraph = new BlockGraph
                {
                    Block = new CYPCore.Consensus.Models.Block(blockHeader.ToHash().ByteToHex(), _serfClient.ClientId,
                        1, buffer),
                    Deps = new List<Dep>(),
                    Prev = new CYPCore.Consensus.Models.Block()
                };

                await _graph.TryAddBlockGraph(blockGraph);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "PoS minting Failed");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactions"></param>
        /// <param name="signature"></param>
        /// <param name="vrfBytes"></param>
        /// <param name="solution"></param>
        /// <param name="bits"></param>
        /// <param name="previous"></param>
        /// <returns></returns>
        private BlockHeaderProto CreateBlock(TransactionProto[] transactions, byte[] signature, byte[] vrfBytes,
            ulong solution, int bits, BlockHeaderProto previous)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();
            Guard.Argument(signature, nameof(signature)).NotNull().MaxCount(96);
            Guard.Argument(vrfBytes, nameof(vrfBytes)).NotNull().MaxCount(32);
            Guard.Argument(solution, nameof(solution)).NotNegative();
            Guard.Argument(bits, nameof(bits)).NotNegative().NotZero();
            Guard.Argument(previous, nameof(previous)).NotNull();

            var p256 = System.Numerics.BigInteger.Parse(_validator.Security256.ToStr());
            var x = System.Numerics.BigInteger.Parse(vrfBytes.ByteToHex(),
                System.Globalization.NumberStyles.AllowHexSpecifier);

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
                Height = 1 + previous.Height,
                Locktime = lockTime,
                LocktimeScript = new Script(Op.GetPushOp(lockTime), OpcodeType.OP_CHECKLOCKTIMEVERIFY).ToString(),
                Nonce = nonce,
                PrevMerkelRoot = previous.MerkelRoot,
                Proof = signature.ByteToHex(),
                Sec = _validator.Security256.ToStr(),
                Seed = _validator.Seed.ByteToHex(),
                Solution = solution,
                Transactions = transactions,
                Version = 0x1,
                VrfSig = vrfBytes.ByteToHex(),
            };

            return blockHeader;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async IAsyncEnumerable<TransactionProto> TransactionsAsync()
        {
            foreach (var transaction in _memoryPool.Range(0, 100))
            {
                var verifyTransaction = await _validator.VerifyTransaction(transaction);
                var verifyTransactionFee = _validator.VerifyTransactionFee(transaction);

                var removed = _memoryPool.Remove(transaction.TxnId);
                if (removed == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Error("Unable to remove the transaction from memory pool {@TxnId}",
                        transaction.TxnId);
                }

                if (verifyTransaction == VerifyResult.UnableToVerify) continue;
                if (verifyTransactionFee == VerifyResult.Succeed) yield return transaction;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="reward"></param>
        /// <returns></returns>
        private async Task<TransactionProto> CoinbaseTransactionAsync(int bits, ulong reward)
        {
            Guard.Argument(bits, nameof(bits)).NotNegative();
            Guard.Argument(reward, nameof(reward)).NotNegative();

            TransactionProto transaction = null;

            using var client = new HttpClient { BaseAddress = new Uri(StakingConfigurationOptions.WalletSettings.Url) };

            try
            {
                var pub = await _signing.GetPublicKey(_signing.DefaultSigningKeyName);
                var sendPayment = new SendPaymentProto
                {
                    Address = StakingConfigurationOptions.WalletSettings.Address,
                    Amount = ((double)bits).ConvertToUInt64(),
                    Credentials = new CredentialsProto
                    {
                        Identifier = StakingConfigurationOptions.WalletSettings.Identifier,
                        Passphrase = StakingConfigurationOptions.WalletSettings.Passphrase
                    },
                    Fee = reward,
                    Memo = $"Coinstake {_serfClient.SerfConfigurationOptions.NodeName}: {pub.ByteToHex()}",
                    SessionType = SessionType.Coinstake
                };

                var maxBytesNeeded = FlatBufferSerializer.Default.GetMaxSize(sendPayment);
                var buffer = new byte[maxBytesNeeded];
                FlatBufferSerializer.Default.Serialize(sendPayment, buffer);

                var byteArrayContent = new ByteArrayContent(buffer);
                byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var response = await client.PostAsync(
                    StakingConfigurationOptions.WalletSettings.SendPaymentEndpoint, byteArrayContent,
                    new System.Threading.CancellationToken());

                var read = response.Content.ReadAsStringAsync().Result;
                var jObject = JObject.Parse(read);
                var jToken = jObject.GetValue("flatbuffer");
                var byteArray =
                    Convert.FromBase64String((jToken ?? throw new InvalidOperationException()).Value<string>());

                if (response.IsSuccessStatusCode)
                    transaction = FlatBufferSerializer.Default.Parse<TransactionProto>(byteArray);
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

            return transaction;
        }
    }
}