// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Blake3;
using CYPCore.Consensus.Models;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Network.Commands;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using CYPCore.Wallet;
using Dawn;
using libsignal.ecc;
using MessagePack;
using Microsoft.Extensions.Options;
using NBitcoin;
using Proto;
using Proto.DependencyInjection;
using Serilog;
using Block = CYPCore.Models.Block;
using BlockHeader = CYPCore.Models.BlockHeader;
using BufferStream = CYPCore.Helper.BufferStream;
using Transaction = CYPCore.Models.Transaction;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IPosMinting
    {
        public bool StakeRunning { get; }
        Transaction Get(in byte[] transactionId);
    }

    /// <summary>
    /// 
    /// </summary>
    internal record CoinStake
    {
        public byte[] TransactionId { get; init; }
        public ulong Solution { get; init; }
        public uint Bits { get; init; }
    }

    /// <summary>
    /// 
    /// </summary>
    internal record Kernel
    {
        public byte[] CalculatedVrfSignature { get; init; }
        public byte[] Hash { get; init; }
        public byte[] VerifiedVrfSignature { get; init; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class PosMinting : IPosMinting, IDisposable
    {
        private const uint BlockProposalTimeFromSeconds = 5;
        private const uint VerifiedDelayFunctionsCancellationTimeoutFromMilliseconds = 32000;

        private readonly ActorSystem _actorSystem;
        private readonly PID _pidShimCommand;
        private readonly PID _pidCryptoKeySign;
        private readonly IMemoryPool _memoryPool;
        private readonly INodeWallet _nodeWallet;
        private readonly IValidator _validator;
        private readonly ISync _sync;
        private readonly ILogger _logger;
        private readonly MemStore<Transaction> _memStoreTransactions = new();
        private readonly AppOptions _options;
        private readonly CancellationToken _stoppingToken;
        private readonly CancellationTokenSource _cancellationToken = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="memoryPool"></param>
        /// <param name="nodeWallet"></param>
        /// <param name="validator"></param>
        /// <param name="sync"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public PosMinting(ActorSystem actorSystem, IMemoryPool memoryPool, INodeWallet nodeWallet, IValidator validator,
            ISync sync, IOptions<AppOptions> options, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidShimCommand = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<ShimCommands>());
            _pidCryptoKeySign = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<CryptoKeySign>());
            _memoryPool = memoryPool;
            _nodeWallet = nodeWallet;
            _validator = validator;
            _sync = sync;
            _options = options.Value;
            _logger = logger.ForContext("SourceContext", nameof(PosMinting));
            if (!_options.Staking.Enabled) return;
            _logger.Information("Staking [ENABLED]");
            _stoppingToken = _cancellationToken.Token;
            Poll();
        }

        /// <summary>
        /// 
        /// </summary>
        public bool StakeRunning { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionId"></param>
        /// <returns></returns>
        public Transaction Get(in byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);
            try
            {
                _memStoreTransactions.TryGet(transactionId, out var transaction);
                return transaction;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to find transaction with {@txnId}", transactionId.ByteToHex());
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        private void Poll()
        {
            if (_stoppingToken.IsCancellationRequested) return;
            Task.Run(async () =>
            {
                while (true)
                {
                    if (_sync.SyncRunning)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                        continue;
                    }

                    break;
                }

                var delay = Task.Delay(TimeSpan.FromSeconds(BlockProposalTimeFromSeconds), _stoppingToken);
                while (!delay.IsCompleted)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }

                await Stake();
            }, _stoppingToken);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="WarningException"></exception>
        /// <exception cref="Exception"></exception>
        private async Task Stake()
        {
            try
            {
                if (_sync.SyncRunning) return;
                StakeRunning = true;
                var heightResponse =
                    await _actorSystem.Root.RequestAsync<BlockHeightResponse>(_pidShimCommand,
                        new BlockHeightRequest());
                var lastBlockResponse =
                    await _actorSystem.Root.RequestAsync<LastBlockResponse>(_pidShimCommand, new LastBlockRequest());
                if (lastBlockResponse.Block is null)
                    throw new WarningException("No previous block available for processing");
                var prevBlock = lastBlockResponse.Block;
                if (_validator.GetAdjustedTimeAsUnixTimestamp(BlockProposalTimeFromSeconds) <=
                    prevBlock.BlockHeader.Locktime)
                {
                    throw new Exception(
                        $"Current staking timeslot is smaller than last searched timestamp {prevBlock.BlockHeader.Locktime}");
                }

                var kernel = await SetupKernel(prevBlock.Hash);
                if (_validator.VerifyKernel(kernel.CalculatedVrfSignature, kernel.Hash) == VerifyResult.Succeed)
                {
                    var coinStake = await SetupCoinstake(kernel);
                    var snapshot = await _memStoreTransactions.GetMemSnapshot().SnapshotAsync()
                        .ToArrayAsync(cancellationToken: _stoppingToken);
                    var transactions = snapshot.OrderBy(t => t.Value.Vtime).Select(x => x.Value).ToImmutableArray();
                    var block = await NewBlock(transactions, kernel, coinStake, prevBlock);
                    if (block is { })
                    {
                        var blockGraph = NewBlockGraph(in block, in prevBlock);
                        if (blockGraph is { })
                        {
                            if (await DoesBlockHeightExits(block, coinStake)) return;
                            var newBlockGraphResponse =
                                await _actorSystem.Root.RequestAsync<NewBlockGraphResponse>(_pidShimCommand,
                                    new NewBlockGraphRequest(blockGraph));
                            if (!newBlockGraphResponse.OK)
                            {
                                _logger.Here().Warning("Unable to send blockgraph request");
                            }

                            await foreach (var (key, _) in _memStoreTransactions.GetMemSnapshot().SnapshotAsync()
                                .WithCancellation(_stoppingToken))
                            {
                                _memStoreTransactions.Delete(key);
                            }
                        }
                    }
                }
                else
                {
                    _logger.Here().Information("Kernel not selected for round {@Round}",
                        Convert.ToUInt64(heightResponse.Count) + 2); // prev round + current + next round
                }
            }
            catch (WarningException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Proof-Of-Stake minting failed");
            }
            finally
            {
                StakeRunning = false;
                Poll();
            }
        }

        /// <summary>
        ///  It's highly likely that this node fell behind when competing for the block.
        /// Ensure we don't send an unnecessary payload, delete the coinstake transaction but
        /// verify and keep regular transactions as new ones could have been added.
        /// </summary>
        /// <param name="block"></param>
        /// <param name="coinStake"></param>
        /// <returns></returns>
        private async Task<bool> DoesBlockHeightExits(Block block, CoinStake coinStake)
        {
            var blockExists = await _validator.BlockHeightExists(block.Height);
            if (blockExists != VerifyResult.AlreadyExists) return false;
            _logger.Here().Warning("Block height already exists");

            // Delete coinstake transaction.
            _memStoreTransactions.Delete(coinStake.TransactionId);

            // Remove any regular transactions that might have already been added to the chain.
            await foreach (var transaction in _memStoreTransactions.GetMemSnapshot().SnapshotAsync()
                .Select(x => x.Value).WithCancellation(_stoppingToken))
            {
                var verifyTransaction = await _validator.VerifyTransaction(transaction);
                if (verifyTransaction != VerifyResult.Succeed)
                {
                    _memStoreTransactions.Delete(transaction.TxnId);
                }
            }

            // Tell the wallet to reload itself as we have deleted the coinstake transaction.
            await _nodeWallet.ReloadQueued();

            // Back to block proposal.
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prevBlockHash"></param>
        /// <returns></returns>
        /// <exception cref="WarningException"></exception>
        private async Task<Kernel> SetupKernel(byte[] prevBlockHash)
        {
            Guard.Argument(prevBlockHash, nameof(prevBlockHash)).NotNull().NotEmpty().MaxCount(32);
            var verifiedTransactions = await _memoryPool.GetVerifiedTransactions(_memStoreTransactions.Count(),
                _options.Staking.TransactionsPerBlock);
            foreach (var verifiedTransaction in verifiedTransactions)
            {
                _memStoreTransactions.Put(verifiedTransaction.TxnId, verifiedTransaction);
            }

            using BufferStream ts = new();
            await foreach (var x in _memStoreTransactions.GetMemSnapshot().SnapshotAsync()
                .WithCancellation(_stoppingToken))
            {
                var (_, transaction) = x;
                try
                {
                    if (transaction is null) return null;
                    var hasAnyErrors = transaction.Validate();
                    if (hasAnyErrors.Any()) return null;
                    ts.Append(transaction.ToStream());
                }
                catch (Exception)
                {
                    if (transaction is { })
                        _logger.Here().Error("Unable to verify the transaction {@TxId}", transaction.TxnId.HexToByte());
                }
            }

            if (ts.Size() == 0) throw new WarningException("Stream size is zero");
            var transactionsHash = Hasher.Hash(ts.ToArray()).HexToByte();
            var kernel = _validator.Kernel(prevBlockHash, transactionsHash);
            var keyPairResponse = await GetKeyPair();
            var calculatedVrfSignatureResponse = await _actorSystem.Root.RequestAsync<CalculateVrfResponse>(
                _pidCryptoKeySign,
                new CalculateVrfRequest(Curve.decodePrivatePoint(keyPairResponse.KeyPair.PrivateKey), kernel));
            var verifyVrfSignatureResponse = await _actorSystem.Root.RequestAsync<VerifyVrfSignatureResponse>(
                _pidCryptoKeySign,
                new VerifyVrfSignatureRequest(Curve.decodePoint(keyPairResponse.KeyPair.PublicKey, 0),
                    calculatedVrfSignatureResponse.Signature, kernel));
            return new Kernel
            {
                CalculatedVrfSignature = calculatedVrfSignatureResponse.Signature,
                Hash = kernel,
                VerifiedVrfSignature = verifyVrfSignatureResponse.Signature
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="kernel"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private async Task<CoinStake> SetupCoinstake(Kernel kernel)
        {
            Guard.Argument(kernel, nameof(kernel)).NotNull();
            var solution = await _validator.Solution(kernel.CalculatedVrfSignature, kernel.Hash);
            if (solution == 0) throw new Exception("Solution is zero");
            var runningDistribution = await _validator.GetRunningDistribution();
            var networkShare = _validator.NetworkShare(solution, runningDistribution);
            var reward = _validator.Reward(solution, runningDistribution);
            var bits = _validator.Difficulty(solution, networkShare);
            var (transaction, message) = await _nodeWallet.CreateTransaction(bits, reward, _options.Staking.RewardAddress);
            if (transaction is null) throw new Exception($"Unable to get new coinstake transaction: {message}");
            _memStoreTransactions.Put(transaction.TxnId, transaction);
            return new CoinStake
            {
                Bits = bits,
                TransactionId = transaction.TxnId,
                Solution = solution
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="block"></param>
        /// <param name="prevBlock"></param>
        /// <returns></returns>
        private BlockGraph NewBlockGraph(in Block block, in Block prevBlock)
        {
            Guard.Argument(block, nameof(block)).NotNull();
            Guard.Argument(prevBlock, nameof(prevBlock)).NotNull();
            try
            {
                var keyPairResponse = GetKeyPair().GetAwaiter().GetResult();
                var nodeIdentifier = Util.ToHashIdentifier(keyPairResponse.KeyPair.PublicKey.ByteToHex());
                var nextData = MessagePackSerializer.Serialize(block);
                var nextDataHash = Hasher.Hash(nextData);
                var prevData = MessagePackSerializer.Serialize(prevBlock);
                var preDataHash = Hasher.Hash(prevData);
                var blockGraph = new BlockGraph
                {
                    Block = new Consensus.Models.Block
                    {
                        Data = nextData,
                        DataHash = nextDataHash.ToString(),
                        Hash = Hasher.Hash(block.Height.ToBytes()).ToString(),
                        Node = nodeIdentifier,
                        Round = block.Height,
                    },
                    Prev = new Consensus.Models.Block
                    {
                        Data = prevData,
                        DataHash = preDataHash.ToString(),
                        Hash = Hasher.Hash(prevBlock.Height.ToBytes()).ToString(),
                        Node = nodeIdentifier,
                        Round = prevBlock.Height
                    }
                };
                return blockGraph;
            }
            catch (Exception)
            {
                _logger.Here().Error("Unable to create new blockgraph");
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactions"></param>
        /// <param name="kernel"></param>
        /// <param name="coinStake"></param>
        /// <param name="previousBlock"></param>
        /// <returns></returns>
        private async Task<Block> NewBlock(ImmutableArray<Transaction> transactions, Kernel kernel, CoinStake coinStake, Block previousBlock)
        {
            Guard.Argument(transactions, nameof(transactions)).NotEmpty();
            Guard.Argument(kernel, nameof(kernel)).NotNull();
            Guard.Argument(kernel.CalculatedVrfSignature, nameof(kernel.CalculatedVrfSignature)).NotNull().MaxCount(96);
            Guard.Argument(kernel.VerifiedVrfSignature, nameof(kernel.VerifiedVrfSignature)).NotNull().MaxCount(32);
            Guard.Argument(coinStake, nameof(coinStake)).NotNull();
            Guard.Argument(coinStake.Solution, nameof(coinStake.Solution)).NotZero().NotNegative();
            Guard.Argument(coinStake.Bits, nameof(coinStake.Bits)).NotZero().NotNegative();
            Guard.Argument(previousBlock, nameof(previousBlock)).NotNull();
            try
            {
                var nonce = await GetNonce(kernel, coinStake, out var ct);
                if (nonce is null) throw new Exception("Unable to create nonce");
                if (ct.IsCancellationRequested) return null;
                var merkelRoot = BlockHeader.ToMerkelRoot(previousBlock.BlockHeader.MerkleRoot, transactions);
                var keyPairResponse = await GetKeyPair();
                var lockTime = _validator.GetAdjustedTimeAsUnixTimestamp(BlockProposalTimeFromSeconds);
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
                        MerkleRoot = merkelRoot,
                        PrevBlockHash = previousBlock.Hash
                    },
                    NrTx = (ushort)transactions.Length,
                    Txs = transactions,
                    BlockPos = new BlockPos
                    {
                        Bits = coinStake.Bits,
                        Nonce = nonce,
                        Solution = coinStake.Solution,
                        VrfProof = kernel.CalculatedVrfSignature,
                        VrfSig = kernel.VerifiedVrfSignature,
                        PublicKey = keyPairResponse.KeyPair.PublicKey
                    },
                    Size = 1
                };
                block.Size = block.GetSize();
                block.Hash = _validator.IncrementHasher(previousBlock.Hash, block.ToHash());
                return block;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to create new block");
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="coinStake"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private Task<byte[]> GetNonce(Kernel kernel, CoinStake coinStake, out CancellationToken cancellationToken)
        {
            Guard.Argument(kernel, nameof(kernel)).NotNull();
            Guard.Argument(coinStake, nameof(coinStake)).NotNull();
            var tcs = new TaskCompletionSource<byte[]>();
            var x = BigInteger.Parse(kernel.VerifiedVrfSignature.ByteToHex(), NumberStyles.AllowHexSpecifier);
            if (x.Sign <= 0)
            {
                x = -x;
            }

            var ct = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(VerifiedDelayFunctionsCancellationTimeoutFromMilliseconds)).Token;
            cancellationToken = ct;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var sloth = new Sloth(ct);
                    var nonce = sloth.Eval((int)coinStake.Bits, x);
                    tcs.SetResult(nonce.ToBytes());
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                    tcs.SetResult(null);
                }
            }, ct);
            return tcs.Task;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<KeyPairResponse> GetKeyPair()
        {
            var keyPairResponse = await _actorSystem.Root.RequestAsync<KeyPairResponse>(_pidCryptoKeySign,
                new KeyPairRequest(CryptoKeySign.DefaultSigningKeyName));
            return keyPairResponse;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _cancellationToken?.Cancel();
            _cancellationToken?.Dispose();
        }
    }
}