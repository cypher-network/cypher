// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blake3;
using CypherNetwork.Consensus.Models;
using CypherNetwork.Cryptography;
using CypherNetwork.Extensions;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using CypherNetwork.Persistence;
using Dawn;
using libsignal.ecc;
using NBitcoin;
using Serilog;
using Block = CypherNetwork.Models.Block;
using BlockHeader = CypherNetwork.Models.BlockHeader;
using Transaction = CypherNetwork.Models.Transaction;

namespace CypherNetwork.Ledger;

/// <summary>
/// </summary>
public interface IPPoS
{
    public bool Running { get; }
    Transaction Get(in byte[] transactionId);
    int Count();
}

/// <summary>
/// </summary>
internal record CoinStake
{
    public Transaction Transaction { get; init; }
    public ulong Solution { get; init; }
    public uint Bits { get; init; }
}

/// <summary>
/// </summary>
internal record Kernel
{
    public byte[] CalculatedVrfSignature { get; init; }
    public byte[] Hash { get; init; }
    public byte[] VerifiedVrfSignature { get; init; }
}

/// <summary>
/// </summary>
public class PPoS : IPPoS, IDisposable
{
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;
    private readonly Caching<Transaction> _syncCacheTransactions = new();
    private readonly ThreadFiber _threadFiber = new(new DefaultQueue(), "CypherNetworkCore-PPoS");
    private bool _disposed;
    private int _running;

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public PPoS(ICypherSystemCore cypherSystemCore, ILogger logger)
    {
        _cypherSystemCore = cypherSystemCore;
        _logger = logger.ForContext("SourceContext", nameof(PPoS));
        const uint again = LedgerConstant.BlockProposalTimeFromSeconds * 1 * 1000;
        _threadFiber.ScheduleOnInterval(() => InitAsync().ConfigureAwait(false), 0, again);
        _threadFiber.Start();
    }

    /// <summary>
    /// </summary>
    public bool Running => _running != 0;

    /// <summary>
    /// </summary>
    /// <param name="transactionId"></param>
    /// <returns></returns>
    public Transaction Get(in byte[] transactionId)
    {
        Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);
        try
        {
            if (_syncCacheTransactions.TryGet(transactionId, out var transaction))
                return transaction;
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to find transaction {@TxId}", transactionId.ByteToHex());
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public int Count()
    {
        return _syncCacheTransactions.Count;
    }

    /// <summary>
    /// </summary>
    private async Task InitAsync()
    {
        if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
        var sync = _cypherSystemCore.Sync();
        if (sync.Running)
        {
            Thread.Sleep(TimeSpan.FromSeconds(LedgerConstant.WaitSyncTimeFromSeconds));
            return;
        }

        if (!_cypherSystemCore.Node.Staking.Enabled)
        {
            Thread.Sleep(TimeSpan.FromSeconds(LedgerConstant.WaitPPoSEnabledTimeFromSeconds));
            return;
        }

        if (sync.Running) return;
        if (Running) return;
        _running = 1;
        await RunStakingAsync();
    }

    /// <summary>
    /// </summary>
    /// <exception cref="Exception"></exception>
    private async Task RunStakingAsync()
    {
        try
        {
            var prevBlock = await GetPreviousBlockAdjustedTimeAsUnixTimestampAsync();
            if (prevBlock is null) return;
            if (!await BlockHeightSynchronizedAsync())
            {
                return;
            }

            var kernel = await CreateKernelAsync(prevBlock.Hash, prevBlock.Height + 1);
            if (kernel is null) return;
            if (_cypherSystemCore.Validator().VerifyKernel(kernel.CalculatedVrfSignature, kernel.Hash) !=
                VerifyResult.Succeed) return;
            _logger.Information("KERNEL <selected> for round [{@Round}]",
                prevBlock.Height + 2); // prev round + current + next round
            var coinStake = await CreateCoinstakeAsync(kernel);
            if (coinStake is null) return;
            RemoveAnyCoinstake();
            _syncCacheTransactions.Add(coinStake.Transaction.TxnId, coinStake.Transaction);
            var transactions = SortTransactions();
            var newBlock = await NewBlockAsync(transactions, kernel, coinStake, prevBlock);
            if (newBlock is null) return;
            var blockGraph = NewBlockGraph(in newBlock, in prevBlock);
            if (blockGraph is null) return;
            var graph = _cypherSystemCore.Graph();
            if (await graph.BlockHeightExistsAsync(new BlockHeightExistsRequest(newBlock.Height)) ==
                VerifyResult.AlreadyExists) return;
            _logger.Information("Publishing... [BLOCKGRAPH]");
            await graph.PostAsync(blockGraph);
            foreach (var transaction in transactions) _syncCacheTransactions.Remove(transaction.TxnId);
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Pure Proof of Stake failed");
        }
        finally
        {
            // Call again in case of an exception.
            RemoveAnyCoinstake();
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task<Block> GetPreviousBlockAdjustedTimeAsUnixTimestampAsync()
    {
        if (await _cypherSystemCore.Graph().GetPreviousBlockAsync() is not { } prevBlock) return null;
        return Helper.Util.GetAdjustedTimeAsUnixTimestamp(LedgerConstant.BlockProposalTimeFromSeconds) >
               prevBlock.BlockHeader.Locktime
            ? prevBlock
            : null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private ImmutableArray<Transaction> SortTransactions()
    {
        var transactions = _syncCacheTransactions.GetItems();
        if (transactions.Length == 0) return ImmutableArray<Transaction>.Empty;
        if (transactions[0].Vtime == null) return transactions.ToArray().ToImmutableArray();
        var n = transactions.Length;
        var aux = new Transaction[n];
        for (var i = 0; i < n; i++) aux[i] = transactions.ElementAt(n - 1 - i);
        return aux.ToImmutableArray();
    }

    /// <summary>
    /// Sanity check if coinstake transaction exists before adding.
    /// </summary>
    private void RemoveAnyCoinstake()
    {
        try
        {
            foreach (var transaction in _syncCacheTransactions.GetItems())
            {
                if (transaction.OutputType() != CoinType.Coinstake) continue;
                _logger.Warning("Removing coinstake transaction");
                _syncCacheTransactions.Remove(transaction.TxnId);
            }
        }
        catch (Exception)
        {
            // Ignore
        }
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task<bool> BlockHeightSynchronizedAsync()
    {
        var peers = await _cypherSystemCore.PeerDiscovery().GetDiscoveryAsync();
        if (!peers.Any()) return true;
        var blockCountResponse = await _cypherSystemCore.Graph().GetBlockCountAsync();
        var maxBlockHeight = peers.Max(x => x.BlockCount);
        return blockCountResponse?.Count >= (long)maxBlockHeight;
    }

    /// <summary>
    /// </summary>
    /// <param name="prevBlockHash"></param>
    /// <param name="round"></param>
    /// <returns></returns>
    private async Task<Kernel> CreateKernelAsync(byte[] prevBlockHash, ulong round)
    {
        Guard.Argument(prevBlockHash, nameof(prevBlockHash)).NotNull().NotEmpty().MaxCount(32);
        var memPool = _cypherSystemCore.MemPool();
        var verifiedTransactions = Array.Empty<Transaction>();
        if (_syncCacheTransactions.Count < _cypherSystemCore.Node.Staking.MaxTransactionsPerBlock)
            verifiedTransactions = await memPool.GetVerifiedTransactionsAsync(
                _cypherNetworkCore.AppOptions.Staking.TransactionsPerBlock - _syncCacheTransactions.Count);
        foreach (var transaction in verifiedTransactions) _syncCacheTransactions.Add(transaction.TxnId, transaction);
        RemoveAnyDuplicateImageKeys();
        await RemoveAnyUnVerifiedTransactionsAsync();
        if (_cypherSystemCore.Graph().HashTransactions(
                new HashTransactionsRequest(SortTransactions().ToArray())) is not { } transactionsHash) return null;
        var kernel = _cypherSystemCore.Validator().Kernel(prevBlockHash, transactionsHash, round);
        var crypto = _cypherSystemCore.Crypto();
        var calculatedVrfSignature = crypto.GetCalculateVrfSignature(
            Curve.decodePrivatePoint(_cypherSystemCore.KeyPair.PrivateKey.FromSecureString().HexToByte()), kernel);
        var verifyVrfSignature = crypto.GetVerifyVrfSignature(
            Curve.decodePoint(_cypherSystemCore.KeyPair.PublicKey, 0), kernel, calculatedVrfSignature);
        _logger.Information("KERNEL <transactions>       [{@Count}]", Count());
        return new Kernel
        {
            CalculatedVrfSignature = calculatedVrfSignature,
            Hash = kernel,
            VerifiedVrfSignature = verifyVrfSignature
        };
    }

    /// <summary>
    /// 
    /// </summary>
    private void RemoveAnyDuplicateImageKeys()
    {
        var noDupImageKeys = new List<byte[]>();
        foreach (var transaction in _syncCacheTransactions.GetItems())
            foreach (var vin in transaction.Vin)
            {
                var vInput = noDupImageKeys.FirstOrDefault(x => x.Xor(vin.Image));
                if (vInput is not null) _syncCacheTransactions.Remove(transaction.TxnId);
                noDupImageKeys.Add(vin.Image);
            }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="previous"></param>
    /// <param name="next"></param>
    /// <returns></returns>
    private static byte[] IncrementHasher(byte[] previous, byte[] next)
    {
        Guard.Argument(previous, nameof(previous)).NotNull().MaxCount(32);
        Guard.Argument(next, nameof(next)).NotNull().MaxCount(32);
        var hasher = Hasher.New();
        hasher.Update(previous);
        hasher.Update(next);
        var hash = hasher.Finalize();
        return hash.AsSpanUnsafe().ToArray();
    }

    /// <summary>
    /// </summary>
    /// <param name="kernel"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<CoinStake> CreateCoinstakeAsync(Kernel kernel)
    {
        Guard.Argument(kernel, nameof(kernel)).NotNull();
        _logger.Information("Begin...      [SOLUTION]");
        var validator = _cypherSystemCore.Validator();
        var solution = await validator.SolutionAsync(kernel.CalculatedVrfSignature, kernel.Hash).ConfigureAwait(false);
        if (solution == 0) return null;
        var height = await _cypherSystemCore.UnitOfWork().HashChainRepository.CountAsync() + 1;
        var networkShare = validator.NetworkShare(solution, (ulong)height);
        var bits = validator.Bits(solution, networkShare);
        _logger.Information("Begin...      [COINSTAKE]");
        var walletTransaction = await _cypherSystemCore.Wallet()
            .CreateTransactionAsync(bits, networkShare.ConvertToUInt64(), _cypherSystemCore.Node.Staking.RewardAddress);
        if (walletTransaction.Transaction is not null)
            return new CoinStake { Bits = bits, Transaction = walletTransaction.Transaction, Solution = solution };
        _logger.Warning("Unable to create coinstake transaction: {@Message}", walletTransaction.Message);
        return null;
    }

    /// <summary>
    /// </summary>
    /// <param name="block"></param>
    /// <param name="prevBlock"></param>
    /// <returns></returns>
    private BlockGraph NewBlockGraph(in Block block, in Block prevBlock)
    {
        Guard.Argument(block, nameof(block)).NotNull();
        Guard.Argument(prevBlock, nameof(prevBlock)).NotNull();
        _logger.Information("Begin...      [BLOCKGRAPH]");
        try
        {
            var nodeIdentifier = _cypherSystemCore.KeyPair.PublicKey.ToHashIdentifier();
            var nextData = block.Serialize();
            var nextDataHash = Hasher.Hash(nextData);
            var prevData = prevBlock.Serialize();
            var preDataHash = Hasher.Hash(prevData);
            var blockGraph = new BlockGraph
            {
                Block = new Consensus.Models.Block
                {
                    BlockHash = block.Hash,
                    Data = nextData,
                    DataHash = nextDataHash.ToString(),
                    Hash = Hasher.Hash(block.Height.ToBytes()).ToString(),
                    Node = nodeIdentifier,
                    Round = block.Height
                },
                Prev = new Consensus.Models.Block
                {
                    BlockHash = prevBlock.Hash,
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
    /// </summary>
    /// <param name="transactions"></param>
    /// <param name="kernel"></param>
    /// <param name="coinStake"></param>
    /// <param name="previousBlock"></param>
    /// <returns></returns>
    private async Task<Block> NewBlockAsync(ImmutableArray<Transaction> transactions, Kernel kernel, CoinStake coinStake,
        Block previousBlock)
    {
        Guard.Argument(transactions, nameof(transactions)).NotEmpty();
        Guard.Argument(kernel, nameof(kernel)).NotNull();
        Guard.Argument(kernel.CalculatedVrfSignature, nameof(kernel.CalculatedVrfSignature)).NotNull().MaxCount(96);
        Guard.Argument(kernel.VerifiedVrfSignature, nameof(kernel.VerifiedVrfSignature)).NotNull().MaxCount(32);
        Guard.Argument(coinStake, nameof(coinStake)).NotNull();
        Guard.Argument(coinStake.Solution, nameof(coinStake.Solution)).NotZero().NotNegative();
        Guard.Argument(coinStake.Bits, nameof(coinStake.Bits)).NotZero().NotNegative();
        Guard.Argument(previousBlock, nameof(previousBlock)).NotNull();
        _logger.Information("Begin...      [BLOCK]");
        try
        {
            var nonce = await GetNonceAsync(kernel, coinStake);
            if (nonce.Length == 0) return null;
            var merkelRoot = BlockHeader.ToMerkleRoot(previousBlock.BlockHeader.MerkleRoot, transactions);
            var lockTime = Helper.Util.GetAdjustedTimeAsUnixTimestamp(LedgerConstant.BlockProposalTimeFromSeconds);
            var block = new Block
            {
                Hash = new byte[32],
                Height = 1 + previousBlock.Height,
                BlockHeader = new BlockHeader
                {
                    Version = 2,
                    Height = previousBlock.BlockHeader.Height,
                    Locktime = lockTime,
                    LocktimeScript =
                        new Script(Op.GetPushOp(lockTime), OpcodeType.OP_CHECKLOCKTIMEVERIFY).ToString().ToBytes(),
                    MerkleRoot = merkelRoot,
                    PrevBlockHash = previousBlock.Hash
                },
                NrTx = (ushort)transactions.Length,
                Txs = transactions.ToArray(),
                BlockPos = new BlockPoS
                {
                    Bits = coinStake.Bits,
                    Nonce = nonce,
                    Solution = coinStake.Solution,
                    VrfProof = kernel.CalculatedVrfSignature,
                    VrfSig = kernel.VerifiedVrfSignature,
                    PublicKey = _cypherSystemCore.KeyPair.PublicKey
                },
                Size = 1
            };

            block.Size = block.GetSize();
            block.Hash = IncrementHasher(previousBlock.Hash, block.ToHash());
            return block;
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to create new block");
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <param name="kernel"></param>
    /// <param name="coinStake"></param>
    /// <returns></returns>
    private async Task<byte[]> GetNonceAsync(Kernel kernel, CoinStake coinStake)
    {
        Guard.Argument(kernel, nameof(kernel)).NotNull();
        Guard.Argument(coinStake, nameof(coinStake)).NotNull();
        _logger.Information("Begin...      [SLOTH]");
        var x = BigInteger.Parse(kernel.VerifiedVrfSignature.ByteToHex(), NumberStyles.AllowHexSpecifier);
        if (x.Sign <= 0) x = -x;
        var nonceHash = Array.Empty<byte>();
        try
        {
            var sloth = new Sloth(LedgerConstant.SlothCancellationTimeoutFromMilliseconds,
                _cypherSystemCore.ApplicationLifetime.ApplicationStopping);
            var nonce = await sloth.EvalAsync((int)coinStake.Bits, x);
            if (!string.IsNullOrEmpty(nonce)) nonceHash = nonce.ToBytes();
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return nonceHash;
    }

    /// <summary>
    ///  Removes any transactions already on chain or are invalid.
    /// </summary>
    /// <returns></returns>
    private async Task RemoveAnyUnVerifiedTransactionsAsync()
    {
        var validator = _cypherSystemCore.Validator();
        foreach (var transaction in _syncCacheTransactions.GetItems())
        {
            if (transaction.OutputType() == CoinType.Coinstake) continue;
            if (await validator.VerifyTransactionAsync(transaction) != VerifyResult.Succeed)
                _syncCacheTransactions.Remove(transaction.TxnId);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="disposing"></param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _threadFiber?.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// 
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}