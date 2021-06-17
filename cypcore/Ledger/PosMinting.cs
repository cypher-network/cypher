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
using Blake3;
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
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == (ulong) height));
            if (prevBlock == null)
            {
                _logger.Here().Information("No previous block available for processing");
                return;
            }
            
            var coinStakeTimestamp = _validator.GetAdjustedTimeAsUnixTimestamp();
            if (coinStakeTimestamp <= prevBlock.BlockHeader.Locktime)
            {
                _logger.Here()
                    .Warning(
                        "Current coinstake time {@Timestamp} is not greater than last search timestamp {@Locktime}",
                        coinStakeTimestamp, prevBlock.BlockHeader.Locktime);
                return;
            }
            
            try
            {
                byte[] hash;
                using (TangramStream ts = new())
                {
                    transactions.ForEach(x =>
                    {
                        if (x != null)
                        {
                            ts.Append(x.ToStream());
                        }
                    });
                    hash = Hasher.Hash(ts.ToArray()).HexToByte();
                }

                var calculateVrfSignature =
                    _signing.CalculateVrfSignature(Curve.decodePrivatePoint(_keyPair.PrivateKey), hash);
                var verifyVrfSignature = _signing.VerifyVrfSignature(Curve.decodePoint(_keyPair.PublicKey, 0),
                    hash, calculateVrfSignature);
                var solution = _validator.Solution(verifyVrfSignature, hash);
                if (solution == 0) return;
                var runningDistribution = await _validator.GetRunningDistribution();
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
                var block = CreateBlock(transactions.ToArray(), calculateVrfSignature, verifyVrfSignature,
                    solution, bits, prevBlock);
                if (block == null)
                {
                    _logger.Here().Fatal("Unable to create the block");
                    return;
                }
                
                var blockGraph = CreateBlockGraph(block, prevBlock);
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
                    new ValueTask<bool>(block.Height == (ulong) height));
                if (prevBlock == null) return;
                var deliveredBlocks =
                    await _unitOfWork.DeliveredRepository.WhereAsync(block =>
                        new ValueTask<bool>(block.Height == (ulong) (height + 1)));
                var blockWinners = deliveredBlocks.Select(deliveredBlock => new BlockWinner
                {
                    Block = deliveredBlock,
                    Finish = new TimeSpan(deliveredBlock.BlockHeader.Locktime).Subtract(new TimeSpan(prevBlock.BlockHeader.Locktime)).Ticks
                }).ToList();
                if (blockWinners.Any() != true) return;
                _logger.Here().Information("RunStakingWinnerAsync");
                var winners = blockWinners.Where(winner =>
                    winner.Finish <= blockWinners.Select(endTime => endTime.Finish).Min()).ToArray();
                var blockWinner = winners.Length switch
                {
                    > 2 => winners.FirstOrDefault(winner =>
                        winner.Block.BlockPos.Bits >= blockWinners.Select(x => x.Block.BlockPos.Bits).Max()),
                    _ => winners.First()
                };
                if (blockWinner != null)
                {
                    _logger.Here().Information("RunStakingWinnerAsync we have a winner");
                    var exists = await _validator.BlockExists(blockWinner.Block);
                    if (exists == VerifyResult.AlreadyExists)
                    {
                        _logger.Here().Error("Block winner already exists");
                        await RemoveDeliveredBlock(blockWinner);
                        return;
                    }

                    var verifyBlockHeader = await _validator.VerifyBlock(blockWinner.Block);
                    if (verifyBlockHeader == VerifyResult.UnableToVerify)
                    {
                        _logger.Here().Error("Unable to verify the block");
                        await RemoveDeliveredBlock(blockWinner);
                        return;
                    }

                    _logger.Here().Information("RunStakingWinnerAsync saving winner");
                    var saved = await _unitOfWork.HashChainRepository.PutAsync(blockWinner.Block.ToIdentifier(),
                        blockWinner.Block);
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
            Guard.Argument(transaction, nameof(transaction)).NotNull();
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
            Guard.Argument(winner, nameof(winner)).NotNull();
            var removed = await _unitOfWork.DeliveredRepository.RemoveAsync(winner.Block.ToIdentifier());
            if (!removed)
            {
                _logger.Here().Error("Unable to remove potential block winner {@MerkelRoot}",
                    winner.Block.Hash);
            }
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
                Block = new CYPCore.Consensus.Models.Block(block.Hash.ByteToHex(), _serfClient.ClientId,
                    block.Height, MessagePackSerializer.Serialize(block)),
                Prev = new CYPCore.Consensus.Models.Block
                {
                    Data = MessagePackSerializer.Serialize(prevBlock),
                    Hash = prevBlock.Hash.ByteToHex(),
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
            var x = System.Numerics.BigInteger.Parse(verifyVrfSignature.ByteToHex(),
                System.Globalization.NumberStyles.AllowHexSpecifier);
            if (x.Sign <= 0)
            {
                x = -x;
            }

            var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
            var sloth = new Sloth(ct);
            var nonce = sloth.Eval((int) bits, x);
            if (ct.IsCancellationRequested) return null;
            var lockTime = _validator.GetAdjustedTimeAsUnixTimestamp();
            var block = new Block
            {
                Hash = new byte[32],
                Height = 1 + previousBlock.Height,
                BlockHeader = new BlockHeader
                {
                    Version = 0x1,
                    Height = 1 + previousBlock.BlockHeader.Height,
                    Locktime = lockTime,
                    LocktimeScript =
                        new Script(Op.GetPushOp(lockTime), OpcodeType.OP_CHECKLOCKTIMEVERIFY).ToString(),
                    MerkleRoot = BlockHeader.ToMerkelRoot(previousBlock.BlockHeader.MerkleRoot, transactions),
                    PrevBlockHash = previousBlock.Hash
                },
                NrTx = (ushort) transactions.Length,
                Txs = transactions,
                BlockPos = new BlockPos
                {
                    Bits = bits,
                    Nonce = nonce.ToBytes(),
                    Solution = solution,
                    VrfProof = calculateVrfSignature,
                    VrfSig = verifyVrfSignature,
                    PublicKey = _keyPair.PublicKey
                }
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