// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Autofac;
using CYPCore.Consensus.Models;
using Dawn;
using libsignal.ecc;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Serilog;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Helper;
using MessagePack;
using Microsoft.Extensions.Hosting;
using BlockHeader = CYPCore.Models.BlockHeader;
using Transaction = CYPCore.Models.Transaction;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IPosMinting : IStartable
    {
    }

    /// <summary>
    /// 
    /// </summary>
    public class PosMinting : IPosMinting
    {
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
        private readonly Timer _decideWinnerTimer;

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

            _stakingTimer = new Timer(async _ => await Staking(), null, TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(10));
            _decideWinnerTimer = new Timer(async _ => await DecideWinner(), null, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(20));

            applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnApplicationStopping()
        {
            _logger.Here().Information("Application stopping");
            _stakingTimer?.Change(Timeout.Infinite, 0);
            _decideWinnerTimer?.Change(Timeout.Infinite, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task Staking()
        {
            if (_sync.SyncRunning) return;
            var transactionModels = _memoryPool.Range(0, _stakingConfigurationOptions.BlockTransactionCount);
            if (_stakingConfigurationOptions.OnOff != true)
            {
                transactionModels.ForEach(x => { _memoryPool.Remove(x); });
                return;
            }

            var transactionTasks = transactionModels.Select(GetValidTransactionAsync);
            transactionModels = await Task.WhenAll(transactionTasks);

            if (transactionModels.Length == 0) return;

            var transactions = transactionModels.ToList();
            var height = await _unitOfWork.HashChainRepository.CountAsync() - 1;
            var prevBlock =
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == height));
            if (prevBlock == null)
            {
                _logger.Here().Information("No previous block available for processing");
                return;
            }

            var coinStakeTimestamp = _validator.GetAdjustedTimeAsUnixTimestamp();
            if (coinStakeTimestamp <= prevBlock.Locktime)
            {
                _logger.Here()
                    .Warning(
                        "Current coinstake time {@Timestamp} is not greater than last search timestamp {@Locktime}",
                        coinStakeTimestamp, prevBlock.Locktime);
                return;
            }

            try
            {
                uint256 hash;
                using (TangramStream ts = new())
                {
                    transactions.ForEach(x =>
                    {
                        if (x != null)
                        {
                            ts.Append(x.Stream());
                        }
                    });
                    hash = NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray());
                }

                var calculateVrfSignature =
                    _signing.CalculateVrfSignature(Curve.decodePrivatePoint(_keyPair.PrivateKey), hash.ToBytes(false));
                var verifyVrfSignature = _signing.VerifyVrfSignature(Curve.decodePoint(_keyPair.PublicKey, 0),
                    hash.ToBytes(false), calculateVrfSignature);
                var runningDistribution = await _validator.GetRunningDistribution();
                var solution = _validator.Solution(verifyVrfSignature, hash.ToBytes(false));
                if (solution == 0) return;
                var networkShare = _validator.NetworkShare(solution, runningDistribution);
                var reward = _validator.Reward(solution, runningDistribution);
                var bits = _validator.Difficulty(solution, networkShare);
                var coinStakeTransaction = await CoinbaseTransactionAsync(bits, reward);
                if (coinStakeTransaction == null)
                {
                    _logger.Here().Error("Unable to create the coinstake transaction");
                    return;
                }

                transactions.Insert(0, coinStakeTransaction);
                var blockHeader = CreateBlock(transactions.ToArray(), calculateVrfSignature, verifyVrfSignature,
                    solution, bits, prevBlock);
                if (blockHeader == null)
                {
                    _logger.Here().Fatal("Unable to create the block");
                    return;
                }

                _validator.Trie.Put(blockHeader.ToHash(), blockHeader.ToHash());
                blockHeader.MerkelRoot = _validator.Trie.GetRootHash().ByteToHex();
                var signature = await _signing.Sign(_signing.DefaultSigningKeyName, blockHeader.ToFinalStream());
                if (signature == null)
                {
                    _logger.Here().Fatal("Unable to sign the block");
                    return;
                }

                blockHeader.Signature = signature.ByteToHex();
                blockHeader.PublicKey = _keyPair.PublicKey.ByteToHex();

                var b = await _unitOfWork.HashChainRepository.PutAsync(blockHeader.ToIdentifier(), blockHeader);

                var blockGraph = CreateBlockGraph(blockHeader, prevBlock);
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
        /// <returns></returns>
        private async Task DecideWinner()
        {
            if (_sync.SyncRunning) return;
            try
            {
                var height = await _unitOfWork.HashChainRepository.CountAsync() - 1;
                var prevBlock = await _unitOfWork.HashChainRepository.GetAsync(block =>
                    new ValueTask<bool>(block.Height == height));
                if (prevBlock == null) return;
                var deliveredBlocks =
                    await _unitOfWork.DeliveredRepository.WhereAsync(block =>
                        new ValueTask<bool>(block.Height == height + 1));
                var blockWinners = deliveredBlocks.Select(deliveredBlock => new BlockWinner
                {
                    BlockHeader = deliveredBlock,
                    Finish = new TimeSpan(deliveredBlock.Locktime).Subtract(new TimeSpan(prevBlock.Locktime)).Ticks
                }).ToList();
                if (blockWinners.Any() != true) return;
                _logger.Here().Information("RunStakingWinnerAsync");
                var winners = blockWinners.Where(winner =>
                    winner.Finish <= blockWinners.Select(endTime => endTime.Finish).Min()).ToArray();
                var blockWinner = winners.Length switch
                {
                    > 2 => winners.FirstOrDefault(winner =>
                        winner.BlockHeader.Bits >= blockWinners.Select(bits => bits.BlockHeader.Bits).Max()),
                    _ => winners.First()
                };
                if (blockWinner != null)
                {
                    _logger.Here().Information("RunStakingWinnerAsync we have a winner");
                    var exists = await _validator.BlockExists(blockWinner.BlockHeader);
                    if (exists == VerifyResult.AlreadyExists)
                    {
                        _logger.Here().Error("Block winner already exists");
                        await RemoveDeliveredBlock(blockWinner);
                        return;
                    }

                    var verifyBlockHeader = await _validator.VerifyBlockHeader(blockWinner.BlockHeader);
                    if (verifyBlockHeader == VerifyResult.UnableToVerify)
                    {
                        _logger.Here().Error("Unable to verify the block");
                        await RemoveDeliveredBlock(blockWinner);
                        return;
                    }

                    _logger.Here().Information("RunStakingWinnerAsync saving winner");
                    var saved = await _unitOfWork.HashChainRepository.PutAsync(blockWinner.BlockHeader.ToIdentifier(),
                        blockWinner.BlockHeader);
                    if (!saved)
                    {
                        _logger.Here().Error("Unable to save the block winner");
                        return;
                    }
                }

                var removeDeliveredBlockTasks = new List<Task>();
                blockWinners.ForEach(winner => { removeDeliveredBlockTasks.Add(RemoveDeliveredBlock(winner)); });
                await Task.WhenAll(removeDeliveredBlockTasks);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Decide stake winner Failed");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        private async Task<Transaction> GetValidTransactionAsync(Transaction transaction)
        {
            try
            {
                var verifyTransaction = await _validator.VerifyTransaction(transaction);
                var verifyTransactionFee = _validator.VerifyTransactionFee(transaction);
                var removed = _memoryPool.Remove(transaction);
                if (removed == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Error("Unable to remove the transaction from the memory pool {@TxnId}",
                        transaction.TxnId);
                }

                if (verifyTransaction == VerifyResult.Succeed && verifyTransactionFee == VerifyResult.Succeed)
                {
                    return transaction;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to verify the transaction");
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="winner"></param>
        /// <returns></returns>
        private async Task RemoveDeliveredBlock(BlockWinner winner)
        {
            var removed = await _unitOfWork.DeliveredRepository.RemoveAsync(winner.BlockHeader.ToIdentifier());
            if (!removed)
            {
                _logger.Here().Error("Unable to remove potential block winner {@MerkelRoot}",
                    winner.BlockHeader.MerkelRoot);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <param name="prevBlockHeader"></param>
        /// <returns></returns>
        private BlockGraph CreateBlockGraph(BlockHeader blockHeader, BlockHeader prevBlockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();
            Guard.Argument(prevBlockHeader, nameof(prevBlockHeader)).NotNull();
            var blockGraph = new BlockGraph
            {
                Block = new CYPCore.Consensus.Models.Block(blockHeader.MerkelRoot, _serfClient.ClientId,
                    (ulong)blockHeader.Height, MessagePackSerializer.Serialize(blockHeader)),
                Prev = new CYPCore.Consensus.Models.Block
                {
                    Data = MessagePackSerializer.Serialize(prevBlockHeader),
                    Hash = prevBlockHeader.MerkelRoot,
                    Node = _serfClient.ClientId,
                    Round = (ulong)prevBlockHeader.Height
                }
            };
            return blockGraph;
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
        private BlockHeader CreateBlock(Transaction[] transactions, byte[] signature, byte[] vrfBytes,
            ulong solution, int bits, BlockHeader previous)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();
            Guard.Argument(signature, nameof(signature)).NotNull().MaxCount(96);
            Guard.Argument(vrfBytes, nameof(vrfBytes)).NotNull().MaxCount(32);
            Guard.Argument(solution, nameof(solution)).NotNegative();
            Guard.Argument(bits, nameof(bits)).NotNegative().NotZero();
            Guard.Argument(previous, nameof(previous)).NotNull();
            var p256 = System.Numerics.BigInteger.Parse(Validator.Security256);
            var x = System.Numerics.BigInteger.Parse(vrfBytes.ByteToHex(),
                System.Globalization.NumberStyles.AllowHexSpecifier);
            if (x.Sign <= 0)
            {
                x = -x;
            }

            var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
            var sloth = new Sloth(ct);
            var nonce = sloth.Eval(bits, x, p256);
            if (ct.IsCancellationRequested) return null;
            var lockTime = _validator.GetAdjustedTimeAsUnixTimestamp();
            var blockHeader = new BlockHeader
            {
                Bits = bits,
                Height = 1 + previous.Height,
                Locktime = lockTime,
                LocktimeScript = new Script(Op.GetPushOp(lockTime), OpcodeType.OP_CHECKLOCKTIMEVERIFY).ToString(),
                Nonce = nonce,
                PrevMerkelRoot = previous.MerkelRoot,
                Proof = signature.ByteToHex(),
                Sec = Validator.Security256,
                Seed = Validator.Seed,
                Solution = solution,
                Transactions = transactions,
                Version = 0x1,
                VrfSignature = vrfBytes.ByteToHex(),
            };
            return blockHeader;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="reward"></param>
        /// <returns></returns>
        private async Task<Transaction> CoinbaseTransactionAsync(int bits, ulong reward)
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
                    Fee = reward,
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
                var read = response.Content.ReadAsStringAsync().Result;
                var jObject = JObject.Parse(read);
                var jToken = jObject.GetValue("messagepack");
                var byteArray =
                    Convert.FromBase64String((jToken ?? throw new InvalidOperationException()).Value<string>());
                if (response.IsSuccessStatusCode)
                    transaction = MessagePackSerializer.Deserialize<Transaction>(byteArray);
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