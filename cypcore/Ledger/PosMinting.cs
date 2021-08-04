// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Blake3;
using CYPCore.Consensus.Models;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using Dawn;
using Dispatch;
using libsignal.ecc;
using MessagePack;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Serilog;
using Block = CYPCore.Models.Block;
using BlockHeader = CYPCore.Models.BlockHeader;
using Transaction = CYPCore.Models.Transaction;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IPosMinting : IStartable
    {
        public bool StakeRunning { get; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class PosMinting : IPosMinting
    {
        public const uint StakeTimeSlot = 0x0000000A;
        public const uint SlothTimeout = 0x0000014;

        private readonly IGraph _graph;
        private readonly IMemoryPool _memoryPool;
        private readonly ISerfClient _serfClient;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signing;
        private readonly IValidator _validator;
        private readonly ISync _sync;
        private readonly ILogger _logger;
        private readonly KeyPair _keyPair;
        private readonly StakingConfigurationOptions _stakingConfigurationOptions;
        private readonly Timer _stakingTimer;
        private readonly SerialQueue _serialQueue;

        public PosMinting(IGraph graph, IMemoryPool memoryPool, ISerfClient serfClient, IUnitOfWork unitOfWork,
            ISigning signing, IValidator validator, ISync sync, StakingConfigurationOptions stakingConfigurationOptions,
            ILogger logger, IHostApplicationLifetime applicationLifetime)
        {
            _graph = graph;
            _memoryPool = memoryPool;
            _serfClient = serfClient;
            _unitOfWork = unitOfWork;
            _signing = signing;
            _validator = validator;
            _sync = sync;
            _stakingConfigurationOptions = stakingConfigurationOptions;
            _logger = logger.ForContext("SourceContext", nameof(PosMinting));
            _keyPair = _signing.GetOrUpsertKeyName(_signing.DefaultSigningKeyName).GetAwaiter().GetResult();

            _serialQueue = new SerialQueue();
            _stakingTimer = new Timer(_ => Staking(), null, TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(StakeTimeSlot));

            applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        }

        public bool StakeRunning { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        private void OnApplicationStopping()
        {
            _logger.Here().Information("Application stopping");
            _stakingTimer?.Change(Timeout.Infinite, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private void Staking()
        {
            if (_sync.SyncRunning) return;
            if (StakeRunning) return;

            async void TryStake()
            {
                try
                {
                    if (_stakingConfigurationOptions.OnOff != true)
                    {
                        _stakingTimer?.Change(Timeout.Infinite, 0);
                        return;
                    }

                    StakeRunning = true;

                    var height = await _unitOfWork.HashChainRepository.GetBlockHeightAsync();
                    var prevBlock = await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == (ulong)height));
                    if (prevBlock == null) throw new WarningException("No previous block available for processing");

                    var coinStakeTimestamp = _validator.GetAdjustedTimeAsUnixTimestamp(StakeTimeSlot);
                    if (coinStakeTimestamp <= prevBlock.BlockHeader.Locktime)
                    {
                        throw new Exception($"Current coinstake time {coinStakeTimestamp} is not greater than last search timestamp {prevBlock.BlockHeader.Locktime}");
                    }

                    var transactionModels = _memoryPool.Range(0, _stakingConfigurationOptions.TransactionsPerBlock);
                    var transactionTasks = transactionModels.Select(GetValidTransactionAsync);
                    transactionModels = await Task.WhenAll(transactionTasks);
                    if (transactionModels.Any() != true) throw new WarningException("No transaction available for processing");
                    var transactions = transactionModels.ToList();

                    using TangramStream ts = new();
                    transactions.ForEach(x =>
                    {
                        try
                        {
                            if (x == null) return;
                            var hasAnyErrors = x.Validate();
                            if (hasAnyErrors.Any()) return;
                            ts.Append(x.ToStream());
                        }
                        catch (Exception)
                        {
                            _logger.Here().Error("Unable to verify the transaction {@TxId}", x.TxnId.HexToByte());
                        }
                    });
                    if (ts.ToArray().Length == 0) throw new Exception("Stream size is zero");
                    var hash = Hasher.Hash(ts.ToArray()).HexToByte();

                    var calculateVrfSignature = _signing.CalculateVrfSignature(Curve.decodePrivatePoint(_keyPair.PrivateKey), hash);
                    var verifyVrfSignature = _signing.VerifyVrfSignature(Curve.decodePoint(_keyPair.PublicKey, 0), hash, calculateVrfSignature);
                    if (_validator.VerifyLotteryWinner(calculateVrfSignature, hash) == VerifyResult.Succeed)
                    {
                        var solution = _validator.Solution(verifyVrfSignature, hash);
                        if (solution == 0) throw new Exception("Solution is zero");
                        var runningDistribution = await _validator.GetRunningDistribution();
                        var networkShare = _validator.NetworkShare(solution, runningDistribution);
                        var reward = _validator.Reward(solution, runningDistribution);
                        var bits = _validator.Difficulty(solution, networkShare);
                        var coinStakeTransaction = await CoinbaseTransactionAsync(bits, reward);
                        if (coinStakeTransaction == null) throw new Exception("Unable to create coinstake transaction");

                        transactions.Insert(0, coinStakeTransaction);

                        var block = CreateBlock(transactions.ToArray(), calculateVrfSignature, verifyVrfSignature, solution, bits, prevBlock);
                        if (block == null) throw new Exception("Unable to create the block");
                        _logger.Here().Information("DateTime:{@DT} Running Distribution:{@RunningDistribution} Solution:{@Solution} Network Share:{@NetworkShare} Reward:{Reward} Bits:{@Bits}", DateTime.UtcNow.ToString(CultureInfo.InvariantCulture), runningDistribution.ToString(CultureInfo.InvariantCulture), solution.ToString(CultureInfo.InvariantCulture), networkShare.ToString(CultureInfo.InvariantCulture), reward.ToString(CultureInfo.InvariantCulture), bits.ToString(CultureInfo.InvariantCulture));

                        await _graph.TryAddBlockGraph(CreateBlockGraph(block, prevBlock));
                        transactions.ForEach(x => _memoryPool.Remove(x));
                    }
                    else
                    {
                        _logger.Here().Information("Staking node was not selected for this round");
                    }
                }
                catch (WarningException ex)
                {
                    _logger.Here().Warning(ex, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Proof-Of-Stake minting failed");
                }
                finally
                {
                    StakeRunning = false;
                }
            }

            _serialQueue.DispatchAsync(TryStake);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        private async Task<Transaction> GetValidTransactionAsync(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            var hasError = false;
            var verifyTransaction = VerifyResult.Unknown;

            try
            {
                verifyTransaction = await _validator.VerifyTransaction(transaction);
                if (verifyTransaction == VerifyResult.Succeed)
                {
                    return transaction;
                }
            }
            catch (Exception ex)
            {
                hasError = true;
                _logger.Here().Error(ex, "Unable to verify the transaction");
            }
            finally
            {
                if (hasError || verifyTransaction == VerifyResult.UnableToVerify)
                {
                    var removed = _memoryPool.Remove(transaction);
                    if (removed != VerifyResult.Succeed)
                    {
                        _logger.Here().Error("Unable to remove memory pool transaction {@TxnId}",
                            transaction.TxnId.ByteToHex());
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="block"></param>
        /// <param name="prevBlock"></param>
        /// <returns></returns>
        private BlockGraph CreateBlockGraph(Block block, Block prevBlock)
        {
            Guard.Argument(block, nameof(block)).NotNull();
            Guard.Argument(prevBlock, nameof(prevBlock)).NotNull();
            var blockGraph = new BlockGraph
            {
                Block = new Consensus.Models.Block(Hasher.Hash(block.Height.ToBytes()).ToString(), _serfClient.ClientId,
                    block.Height, MessagePackSerializer.Serialize(block)),
                Prev = new Consensus.Models.Block
                {
                    Data = MessagePackSerializer.Serialize(prevBlock),
                    Hash = Hasher.Hash(prevBlock.Height.ToBytes()).ToString(),
                    Node = _serfClient.ClientId,
                    Round = prevBlock.Height
                }
            };
            return blockGraph;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactions"></param>
        /// <param name="calculateVrfSignature"></param>
        /// <param name="verifyVrfSignature"></param>
        /// <param name="solution"></param>
        /// <param name="bits"></param>
        /// <param name="previousBlock"></param>
        /// <returns></returns>
        private Block CreateBlock(Transaction[] transactions, byte[] calculateVrfSignature, byte[] verifyVrfSignature,
            ulong solution, uint bits, Block previousBlock)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();
            Guard.Argument(calculateVrfSignature, nameof(calculateVrfSignature)).NotNull().MaxCount(96);
            Guard.Argument(verifyVrfSignature, nameof(verifyVrfSignature)).NotNull().MaxCount(32);
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(bits, nameof(bits)).NotZero().NotNegative();
            Guard.Argument(previousBlock, nameof(previousBlock)).NotNull();
            var x = BigInteger.Parse(verifyVrfSignature.ByteToHex(),
                NumberStyles.AllowHexSpecifier);
            if (x.Sign <= 0)
            {
                x = -x;
            }

            var ct = new CancellationTokenSource(TimeSpan.FromSeconds(SlothTimeout)).Token;
            var sloth = new Sloth(ct);
            var nonce = sloth.Eval((int)bits, x);
            if (ct.IsCancellationRequested) return null;
            var lockTime = _validator.GetAdjustedTimeAsUnixTimestamp(StakeTimeSlot);
            var block = new Block
            {
                Hash = new byte[32],
                Height = 1 + previousBlock.Height,
                BlockHeader = new BlockHeader
                {
                    Version = 0x2,
                    Height = 1 + previousBlock.BlockHeader.Height,
                    Locktime = lockTime,
                    LocktimeScript =
                        new Script(Op.GetPushOp(lockTime), OpcodeType.OP_CHECKLOCKTIMEVERIFY).ToString(),
                    MerkleRoot = BlockHeader.ToMerkelRoot(previousBlock.BlockHeader.MerkleRoot, transactions),
                    PrevBlockHash = previousBlock.Hash
                },
                NrTx = (ushort)transactions.Length,
                Txs = transactions,
                BlockPos = new BlockPos
                {
                    Bits = bits,
                    Nonce = nonce.ToBytes(),
                    Solution = solution,
                    VrfProof = calculateVrfSignature,
                    VrfSig = verifyVrfSignature,
                    PublicKey = _keyPair.PublicKey
                },
                Size = 1
            };

            block.Size = block.GetSize();
            block.Hash = _validator.IncrementHasher(previousBlock.Hash, block.ToHash());

            return block;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="reward"></param>   
        /// <returns></returns>
        private async Task<Transaction> CoinbaseTransactionAsync(uint bits, ulong reward)
        {
            Guard.Argument(bits, nameof(bits)).NotNegative();
            Guard.Argument(reward, nameof(reward)).NotNegative();
            Transaction transaction = null;
            using var client = new HttpClient { BaseAddress = new Uri(_stakingConfigurationOptions.WalletSettings.Url) };
            try
            {
                var pub = await _signing.GetPublicKey(_signing.DefaultSigningKeyName);
                var sendPayment = new Payment
                {
                    Address = _stakingConfigurationOptions.WalletSettings.Address,
                    Amount = ((decimal)bits).ConvertToUInt64(),
                    Credentials = new Credentials
                    {
                        Identifier = _stakingConfigurationOptions.WalletSettings.Identifier,
                        Passphrase = _stakingConfigurationOptions.WalletSettings.Passphrase
                    },
                    Reward = reward,
                    Memo =
                        $"Coinstake {_serfClient.SerfConfigurationOptions.NodeName}: {pub.ByteToHex().ShorterString()}",
                    SessionType = SessionType.Coinstake
                };

                var buffer = MessagePackSerializer.Serialize(sendPayment);
                var byteArrayContent = new ByteArrayContent(buffer);
                byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var response = await client.PostAsync(
                    _stakingConfigurationOptions.WalletSettings.SendPaymentEndpoint, byteArrayContent,
                    new CancellationToken());
                if (response.IsSuccessStatusCode)
                {
                    var read = response.Content.ReadAsStringAsync().Result;
                    var jObject = JObject.Parse(read);
                    var jToken = jObject.GetValue("messagepack");
                    var byteArray =
                        Convert.FromBase64String((jToken ?? throw new InvalidOperationException()).Value<string>());
                    transaction = MessagePackSerializer.Deserialize<Transaction>(byteArray);
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.Here().Error("{@Content}\n StatusCode: {@StatusCode}", content, (int)response.StatusCode);
                    throw new Exception(content);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to create the coinstake transaction");
            }

            return transaction;
        }

        public void Start()
        {
            // Empty
        }
    }
}