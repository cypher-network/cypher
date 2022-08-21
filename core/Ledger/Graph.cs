// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Blake3;
using CypherNetwork.Consensus;
using CypherNetwork.Consensus.Models;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using CypherNetwork.Persistence;
using Dawn;
using MessagePack;
using NBitcoin;
using Serilog;
using Block = CypherNetwork.Models.Block;
using Interpreted = CypherNetwork.Consensus.Models.Interpreted;
using Transaction = CypherNetwork.Models.Transaction;
using Util = CypherNetwork.Helper.Util;

namespace CypherNetwork.Ledger;

/// <summary>
/// </summary>
public interface IGraph
{
    Task<TransactionBlockIndexResponse> GetTransactionBlockIndexAsync(
        TransactionBlockIndexRequest transactionIndexRequest);

    Task<TransactionResponse> GetTransactionAsync(TransactionRequest transactionRequest);
    Task<Block> GetPreviousBlockAsync();
    Task<SafeguardBlocksResponse> GetSafeguardBlocksAsync(SafeguardBlocksRequest safeguardBlocksRequest);
    Task<BlockHeightResponse> GetBlockHeightAsync();
    Task<BlockCountResponse> GetBlockCountAsync();
    Task<SaveBlockResponse> SaveBlockAsync(SaveBlockRequest saveBlockRequest);
    Task<BlocksResponse> GetBlocksAsync(BlocksRequest blocksRequest);
    Task PublishAsync(BlockGraph blockGraph);
    Task<BlockResponse> GetBlockAsync(byte[] hash);
    Task<VerifyResult> BlockHeightExistsAsync(ulong height);
    Task<VerifyResult> BlockExistsAsync(byte[] hash);
    byte[] HashTransactions(Transaction[] transactions);
}

/// <summary>
/// </summary>
internal record SeenBlockGraph
{
    public long Timestamp { get; } = Util.GetAdjustedTimeAsUnixTimestamp();
    public ulong Round { get; init; }
    public byte[] Hash { get; init; }
}

/// <summary>
/// </summary>
public sealed class Graph : IGraph, IDisposable
{
    private class BlockGraphEventArgs : EventArgs
    {
        public BlockGraph BlockGraph { get; }

        public BlockGraphEventArgs(BlockGraph blockGraph)
        {
            BlockGraph = blockGraph;
        }
    }

    private readonly ActionBlock<BlockGraph> _action;
    private readonly ICypherNetworkCore _cypherNetworkCore;
    private readonly ILogger _logger;
    private readonly IObservable<EventPattern<BlockGraphEventArgs>> _onRoundCompleted;
    private readonly IDisposable _onRoundListener;
    private readonly Caching<BlockGraph> _syncCacheBlockGraph = new();
    private readonly Caching<Block> _syncCacheDelivered = new();
    private readonly Caching<SeenBlockGraph> _syncCacheSeenBlockGraph = new();
    private IDisposable _disposableHandelSeenBlockGraphs;
    private bool _disposed;

    private static readonly object LockOnReady = new();

    /// <summary>
    /// </summary>
    private EventHandler<BlockGraphEventArgs> _onRoundCompletedEventHandler;

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public Graph(ICypherNetworkCore cypherNetworkCore, ILogger logger)
    {
        _cypherNetworkCore = cypherNetworkCore;
        _logger = logger.ForContext("SourceContext", nameof(Graph));
        _onRoundCompleted = Observable.FromEventPattern<BlockGraphEventArgs>(ev => _onRoundCompletedEventHandler += ev,
            ev => _onRoundCompletedEventHandler -= ev);
        _onRoundListener = OnRoundListener();
        var dataflowBlockOptions = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = 1,
            BoundedCapacity = 1
        };
        _action = new ActionBlock<BlockGraph>(NewBlockGraphAsync, dataflowBlockOptions);
        Init();
    }

    /// <summary>
    /// </summary>
    /// <param name="blockGraph"></param>
    public async Task PublishAsync(BlockGraph blockGraph)
    {
        await _action.SendAsync(blockGraph);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="transactionIndexRequest"></param>
    /// <returns></returns>
    public async Task<TransactionBlockIndexResponse> GetTransactionBlockIndexAsync(
        TransactionBlockIndexRequest transactionIndexRequest)
    {
        Guard.Argument(transactionIndexRequest, nameof(transactionIndexRequest)).NotNull();
        try
        {
            var unitOfWork = await _cypherNetworkCore.UnitOfWork();
            var block = await unitOfWork.HashChainRepository.GetAsync(x =>
                new ValueTask<bool>(x.Txs.Any(t => t.TxnId.Xor(transactionIndexRequest.TransactionId))));
            if (block is { })
            {
                return new TransactionBlockIndexResponse(block.Height);
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex.Message);
        }

        return new TransactionBlockIndexResponse(0);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="transactionRequest"></param>
    /// <returns></returns>
    public async Task<TransactionResponse> GetTransactionAsync(TransactionRequest transactionRequest)
    {
        Guard.Argument(transactionRequest, nameof(transactionRequest)).NotNull();
        try
        {
            var unitOfWork = await _cypherNetworkCore.UnitOfWork();
            var blocks = await unitOfWork.HashChainRepository.WhereAsync(x =>
                new ValueTask<bool>(x.Txs.Any(t => t.TxnId.Xor(transactionRequest.TransactionId))));
            var block = blocks.FirstOrDefault();
            var transaction = block?.Txs.FirstOrDefault(x => x.TxnId.Xor(transactionRequest.TransactionId));
            if (transaction is { })
            {
                return new TransactionResponse(transaction);
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex.Message);
        }

        return new TransactionResponse(null);
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public async Task<Block> GetPreviousBlockAsync()
    {
        var unitOfWork = await _cypherNetworkCore.UnitOfWork();
        var height = await unitOfWork.HashChainRepository.GetBlockHeightAsync();
        var prevBlock =
            await unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == (ulong)height));
        return prevBlock;
    }

    /// <summary>
    /// </summary>
    /// <param name="safeguardBlocksRequest"></param>
    /// <returns></returns>
    public async Task<SafeguardBlocksResponse> GetSafeguardBlocksAsync(SafeguardBlocksRequest safeguardBlocksRequest)
    {
        Guard.Argument(safeguardBlocksRequest, nameof(safeguardBlocksRequest)).NotNull();
        try
        {
            var unitOfWork = await _cypherNetworkCore.UnitOfWork();
            var height = (await GetBlockHeightAsync()).Count - safeguardBlocksRequest.NumberOfBlocks;
            height = height < 0x0 ? 0x0 : height;
            var blocks = await unitOfWork.HashChainRepository.OrderByRangeAsync(x => x.Height, (int)height,
                safeguardBlocksRequest.NumberOfBlocks);
            if (blocks.Any()) return new SafeguardBlocksResponse(blocks, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return new SafeguardBlocksResponse(new List<Block>(Array.Empty<Block>()), "Sequence contains zero elements");
    }

    /// <summary>
    /// </summary>
    /// <param name="hash"></param>
    /// <returns></returns>
    public async Task<BlockResponse> GetBlockAsync(byte[] hash)
    {
        try
        {
            var block = await (await _cypherNetworkCore.UnitOfWork()).HashChainRepository.GetAsync(hash);
            if (block is { }) return new BlockResponse(block);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return new BlockResponse(null);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public async Task<BlockCountResponse> GetBlockCountAsync()
    {
        try
        {
            var height = await (await _cypherNetworkCore.UnitOfWork()).HashChainRepository.CountAsync();
            return new BlockCountResponse(height);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return new BlockCountResponse(0);
    }

    /// <summary>
    /// </summary>
    /// <param name="blocksRequest"></param>
    public async Task<BlocksResponse> GetBlocksAsync(BlocksRequest blocksRequest)
    {
        Guard.Argument(blocksRequest, nameof(blocksRequest)).NotNull();
        try
        {
            var unitOfWork = await _cypherNetworkCore.UnitOfWork();
            var (skip, take) = blocksRequest;
            var blocks = await unitOfWork.HashChainRepository.OrderByRangeAsync(x => x.Height, skip, take);
            if (blocks.Any()) return new BlocksResponse(blocks);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return new BlocksResponse(null);
    }

    /// <summary>
    /// </summary>
    public async Task<BlockHeightResponse> GetBlockHeightAsync()
    {
        try
        {
            var count = (await GetBlockCountAsync()).Count;
            if (count > 0) count--;
            return new BlockHeightResponse(count);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return new BlockHeightResponse(0);
    }

    /// <summary>
    /// </summary>
    /// <param name="saveBlockRequest"></param>
    public async Task<SaveBlockResponse> SaveBlockAsync(SaveBlockRequest saveBlockRequest)
    {
        Guard.Argument(saveBlockRequest, nameof(saveBlockRequest)).NotNull();
        try
        {
            if (await _cypherNetworkCore.Validator().VerifyBlockAsync(saveBlockRequest.Block) != VerifyResult.Succeed)
            {
                return new SaveBlockResponse(false);
            }
            var unitOfWork = await _cypherNetworkCore.UnitOfWork();
            if (await unitOfWork.HashChainRepository.PutAsync(saveBlockRequest.Block.Hash, saveBlockRequest.Block))
                return new SaveBlockResponse(true);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return new SaveBlockResponse(false);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="height"></param>
    /// <returns></returns>
    public async Task<VerifyResult> BlockHeightExistsAsync(ulong height)
    {
        Guard.Argument(height, nameof(height)).NotNegative();
        var unitOfWork = await _cypherNetworkCore.UnitOfWork();
        var seen = await unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == height));
        return seen is not null ? VerifyResult.AlreadyExists : VerifyResult.Succeed;
    }

    /// <summary>
    /// </summary>
    /// <param name="hash"></param>
    /// <returns></returns>
    public async Task<VerifyResult> BlockExistsAsync(byte[] hash)
    {
        Guard.Argument(hash, nameof(hash)).NotEmpty().NotEmpty().MaxCount(64);
        var unitOfWork = await _cypherNetworkCore.UnitOfWork();
        var seen = await unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Hash.Xor(hash)));
        return seen is not null ? VerifyResult.AlreadyExists : VerifyResult.Succeed;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="transactions"></param>
    /// <returns></returns>
    public byte[] HashTransactions(Transaction[] transactions)
    {
        if (transactions.Length == 0) return null;
        using BufferStream ts = new();
        foreach (var transaction in transactions) ts.Append(transaction.ToStream());
        return Hasher.Hash(ts.ToArray()).HexToByte();
    }

    /// <summary>
    /// </summary>
    private void Init()
    {
        HandelSeenBlockGraphs();
    }

    /// <summary>
    /// </summary>
    /// <param name="blockGraph"></param>
    private async Task NewBlockGraphAsync(BlockGraph blockGraph)
    {
        Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
        if (blockGraph.Block.Round != await NextRoundAsync()) return;
        if (await BlockHeightExistsAsync(blockGraph.Block.Round) != VerifyResult.Succeed) return;
        if (!_syncCacheSeenBlockGraph.Contains(blockGraph.ToIdentifier()))
        {
            _syncCacheSeenBlockGraph.Add(blockGraph.ToIdentifier(),
                new SeenBlockGraph { Hash = blockGraph.ToHash(), Round = blockGraph.Block.Round });
            await FinalizeAsync(blockGraph);
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="e"></param>
    private void OnRoundReady(BlockGraphEventArgs e)
    {
        if (e.BlockGraph.Block.Round == NextRound()) _onRoundCompletedEventHandler?.Invoke(this, e);
    }

    /// <summary>
    /// </summary>
    private void HandelSeenBlockGraphs()
    {
        _disposableHandelSeenBlockGraphs = Observable.Interval(TimeSpan.FromHours(1)).Subscribe(_ =>
        {
            if (_cypherNetworkCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            try
            {
                var removeSeenBlockGraphBeforeTimestamp = Util.GetUtcNow().AddHours(-1).ToUnixTimestamp();
                var removingBlockGraphs = AsyncHelper.RunSync(async delegate
                {
                    return await _syncCacheSeenBlockGraph.WhereAsync(x =>
                        new ValueTask<bool>(x.Value.Timestamp < removeSeenBlockGraphBeforeTimestamp));
                });
                foreach (var (key, _) in removingBlockGraphs.OrderBy(x => x.Value.Round))
                    _syncCacheSeenBlockGraph.Remove(key);
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                _logger.Here().Error("{@Message}", ex.Message);
            }
        });
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private IDisposable OnRoundListener()
    {
        var onRoundCompletedSubscription = _onRoundCompleted
            .Where(data => data.EventArgs.BlockGraph.Block.Round == NextRound())
            .Throttle(TimeSpan.FromSeconds(LedgerConstant.OnRoundThrottleFromSeconds), NewThreadScheduler.Default)
            .Subscribe(_ =>
            {
                Monitor.Enter(LockOnReady);

                try
                {
                    var blockGraphs = _syncCacheBlockGraph.Where(x => x.Value.Block.Round == NextRound()).ToList();
                    if (blockGraphs.Count < 2) return;
                    var nodeCount = blockGraphs.Select(n => n.Value.Block.Node).Distinct().Count();
                    var f = (nodeCount - 1) / 3;
                    var quorum2F1 = 2 * f + 1;
                    if (nodeCount < quorum2F1) return;
                    var lastInterpreted = GetRound();
                    var config = new Config(lastInterpreted, Array.Empty<ulong>(),
                        _cypherNetworkCore.KeyPair.PublicKey.ToHashIdentifier(), (ulong)nodeCount);
                    var blockmania = new Blockmania(config, _logger) { NodeCount = nodeCount };
                    blockmania.TrackingDelivered.Subscribe(x =>
                    {
                        OnDeliveredReadyAsync(x.EventArgs.Interpreted).SafeFireAndForget();
                    });
                    foreach (var next in blockGraphs)
                    {
                        AsyncHelper.RunSync(async () =>
                        {
                            var (key, blockGraph) = next;
                            await blockmania.AddAsync(blockGraph,
                                _cypherNetworkCore.ApplicationLifetime.ApplicationStopping);
                            _syncCacheBlockGraph.Remove(key);
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Process add blockmania error");
                }
                finally
                {
                    Monitor.Exit(LockOnReady);
                }
            }, exception => { _logger.Here().Error(exception, "Subscribe try add blockmania listener error"); });
        return onRoundCompletedSubscription;
    }

    /// <summary>
    /// </summary>
    /// <param name="blockGraph"></param>
    /// <returns></returns>
    private bool Save(BlockGraph blockGraph)
    {
        Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
        try
        {
            if (_cypherNetworkCore.Validator().VerifyBlockGraphSignatureNodeRound(blockGraph) != VerifyResult.Succeed)
            {
                _logger.Error("Unable to verify block for {@Node} and round {@Round}", blockGraph.Block.Node,
                    blockGraph.Block.Round);
                _syncCacheBlockGraph.Remove(blockGraph.ToIdentifier());
                return false;
            }

            _syncCacheBlockGraph.Add(blockGraph.ToIdentifier(), blockGraph);
        }
        catch (Exception)
        {
            _logger.Here().Error("Unable to save block for {@Node} and round {@Round}", blockGraph.Block.Node,
                blockGraph.Block.Round);
            return false;
        }

        return true;
    }

    /// <summary>
    /// </summary>
    /// <param name="blockGraph"></param>
    /// <returns></returns>
    private async Task<BlockGraph> SignAsync(BlockGraph blockGraph)
    {
        Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
        try
        {
            var (signature, publicKey) = await _cypherNetworkCore.Crypto()
                .SignAsync(_cypherNetworkCore.AppOptions.Network.SigningKeyRingName, blockGraph.ToHash());
            blockGraph.PublicKey = publicKey;
            blockGraph.Signature = signature;
            return blockGraph;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <param name="blockGraph"></param>
    /// <returns></returns>
    private BlockGraph Copy(BlockGraph blockGraph)
    {
        Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
        try
        {
            var localNodeId = _cypherNetworkCore.KeyPair.PublicKey.ToHashIdentifier();
            var copy = new BlockGraph
            {
                Block = new Consensus.Models.Block
                {
                    Data = blockGraph.Block.Data,
                    DataHash = blockGraph.Block.DataHash,
                    Hash = blockGraph.Block.Hash,
                    Node = localNodeId,
                    Round = blockGraph.Block.Round
                },
                Prev = new Consensus.Models.Block
                {
                    Data = blockGraph.Prev.Data,
                    DataHash = blockGraph.Prev.DataHash,
                    Hash = blockGraph.Prev.Hash,
                    Node = localNodeId,
                    Round = blockGraph.Prev.Round
                }
            };
            return copy;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task FinalizeAsync(BlockGraph blockGraph)
    {
        Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
        try
        {
            var copy = blockGraph.Block.Node != _cypherNetworkCore.KeyPair.PublicKey.ToHashIdentifier();
            if (copy)
            {
                _logger.Information("BlockGraph Copy: [{@Node}] Round: [{@Round}]", blockGraph.Block.Node,
                    blockGraph.Block.Round);
                if (!Save(blockGraph)) return;
                var copyBlockGraph = Copy(blockGraph);
                if (copyBlockGraph is null) return;
                var signBlockGraph = await SignAsync(copyBlockGraph);
                if (signBlockGraph is null) return;
                if (!Save(signBlockGraph)) return;
                await BroadcastAsync(signBlockGraph);
                OnRoundReady(new BlockGraphEventArgs(blockGraph));
            }
            else
            {
                _logger.Information("BlockGraph Self: [{@Node}] Round: [{@Round}]", blockGraph.Block.Node,
                    blockGraph.Block.Round);
                var signBlockGraph = await SignAsync(blockGraph);
                if (signBlockGraph is null) return;
                if (!Save(signBlockGraph)) return;
                await BroadcastAsync(signBlockGraph);
            }
        }
        catch (Exception)
        {
            _logger.Here().Error("Unable to add block for {@Node} and round {@Round}", blockGraph.Block.Node,
                blockGraph.Block.Round);
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="deliver"></param>
    /// <returns></returns>
    private async Task OnDeliveredReadyAsync(Interpreted deliver)
    {
        Guard.Argument(deliver, nameof(deliver)).NotNull();
        _logger.Information("Delivered: {@Count} Consumed: {@Consumed} Round: {@Round}", deliver.Blocks.Count,
            deliver.Consumed, deliver.Round);
        var blocks = deliver.Blocks.Where(x => x.Data is { });
        foreach (var deliveredBlock in blocks)
            try
            {
                if (deliveredBlock.Round != await NextRoundAsync()) continue;
                var block = MessagePackSerializer.Deserialize<Block>(deliveredBlock.Data);
                _syncCacheDelivered.AddOrUpdate(block.Hash, block);
            }
            catch (Exception ex)
            {
                _logger.Here().Error("{@Message}", ex.Message);
            }
        await DecideWinnerAsync();
    }

    /// <summary>
    /// </summary>
    private async Task DecideWinnerAsync()
    {
        Block[] deliveredBlocks = null;
        try
        {
            deliveredBlocks = _syncCacheDelivered.Where(x => x.Value.Height == NextRound()).Select(n => n.Value)
                .ToArray();
            if (deliveredBlocks.Any() != true) return;
            _logger.Information("DecideWinnerAsync");
            var winners = deliveredBlocks.Where(x =>
                x.BlockPos.Solution == deliveredBlocks.Select(n => n.BlockPos.Solution).Min()).ToArray();
            _logger.Information("Potential winners");
            foreach (var winner in winners)
                _logger.Here().Information("Hash {@Hash} Solution {@Sol} Node {@Node}", winner.Hash.ByteToHex(),
                    winner.BlockPos.Solution, winner.BlockPos.PublicKey.ToHashIdentifier());
            var block = winners.Length switch
            {
                > 2 => winners.FirstOrDefault(winner =>
                    winner.BlockPos.Solution >= deliveredBlocks.Select(x => x.BlockPos.Solution).Max()),
                _ => winners[0]
            };
            if (block is { })
            {
                if (block.Height != await NextRoundAsync()) return;
                if (block.BlockPos.PublicKey.ToHashIdentifier() ==
                    (await _cypherNetworkCore.PeerDiscovery()).GetLocalNode().Identifier)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(@"  
    _  _         ____    _                  _       __        __  _                                     _  _   
  _| || |_      | __ )  | |   ___     ___  | | __   \ \      / / (_)  _ __    _ __     ___   _ __     _| || |_ 
 |_  ..  _|     |  _ \  | |  / _ \   / __| | |/ /    \ \ /\ / /  | | | '_ \  | '_ \   / _ \ | '__|   |_  ..  _|
 |_      _|     | |_) | | | | (_) | | (__  |   <      \ V  V /   | | | | | | | | | | |  __/ | |      |_      _|
   |_||_|       |____/  |_|  \___/   \___| |_|\_\      \_/\_/    |_| |_| |_| |_| |_|  \___| |_|        |_||_|  ");
                    Console.WriteLine();
                    Console.ResetColor();
                }
                else
                {
                    _logger.Information("We have a winner {@Hash}", block.Hash.ByteToHex());
                }

                if (await BlockHeightExistsAsync(block.Height) == VerifyResult.AlreadyExists)
                {
                    _logger.Error("Block winner already exists");
                    return;
                }

                _logger.Here().Information("Saved in decide winner");
                var saveBlockResponse = await SaveBlockAsync(new SaveBlockRequest(block));
                if (!saveBlockResponse.Ok) _logger.Error("Unable to save the block winner");

                (await _cypherNetworkCore.WalletSession()).Notify(block.Txs.ToArray());
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Decide winner failed");
        }
        finally
        {
            if (deliveredBlocks is { })
                foreach (var block in deliveredBlocks)
                    _syncCacheDelivered.Remove(block.Hash);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private ulong GetRound()
    {
        return AsyncHelper.RunSync(GetRoundAsync);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private async Task<ulong> GetRoundAsync()
    {
        var blockHeightResponse = await GetBlockHeightAsync();
        return (ulong)blockHeightResponse.Count;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private ulong NextRound()
    {
        return AsyncHelper.RunSync(NextRoundAsync);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private async Task<ulong> NextRoundAsync()
    {
        return await GetRoundAsync() + 1;
    }

    /// <summary>
    /// </summary>
    /// <param name="blockGraph"></param>
    /// <returns></returns>
    private async Task BroadcastAsync(BlockGraph blockGraph)
    {
        Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
        try
        {
            if (blockGraph.Block.Round == await NextRoundAsync())
                await _cypherNetworkCore.Broadcast().PublishAsync((TopicType.AddBlockGraph,
                    MessagePackSerializer.Serialize(blockGraph)));
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Broadcast error");
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
            _onRoundListener?.Dispose();
            _disposableHandelSeenBlockGraphs?.Dispose();
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