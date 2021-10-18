// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CYPCore.Consensus;
using CYPCore.Consensus.Models;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Network.Commands;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using Dawn;
using MessagePack;
using Microsoft.Extensions.Hosting;
using Proto;
using Proto.DependencyInjection;
using Serilog;
using Block = CYPCore.Models.Block;
using Interpreted = CYPCore.Consensus.Models.Interpreted;
using MemberState = CYPCore.GossipMesh.MemberState;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IGraph
    {
        Task<VerifyResult> NewBlockGraph(BlockGraph blockGraph);
        Task<Transaction> Get(byte[] transactionId);
        void Dispose();
    }

    /// <summary>
    /// 
    /// </summary>
    public sealed class Graph: IGraph, IDisposable
    {
        private const double OnRoundThrottleFromSeconds = 1.5;

        private readonly ActorSystem _actorSystem;
        private readonly PID _pidShimCommand;
        private readonly PID _pidLocalNode;
        private readonly PID _pidCryptoKeySign;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly IObservable<EventPattern<BlockGraphEventArgs>> _onRoundCompleted;
        private readonly IDisposable _onRoundListener;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly MemStore<BlockGraph> _memStoreBlockGraph = new();
        private readonly MemStore<Block> _memStoreDelivered = new();
        /// <summary>
        /// 
        /// </summary>
        private class BlockGraphEventArgs : EventArgs
        {
            public BlockGraph BlockGraph { get; }

            public BlockGraphEventArgs(BlockGraph blockGraph)
            {
                BlockGraph = blockGraph;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private EventHandler<BlockGraphEventArgs> _onRoundCompletedEventHandler;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="validator"></param>
        /// <param name="applicationLifetime"></param>
        /// <param name="logger"></param>
        public Graph(ActorSystem actorSystem, IValidator validator,
            IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidShimCommand = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<ShimCommands>());
            _pidLocalNode = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<LocalNode>());
            _pidCryptoKeySign = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<CryptoKeySign>());
            _validator = validator;
            _logger = logger.ForContext("SourceContext", nameof(Graph));
            _applicationLifetime = applicationLifetime;
            _onRoundCompleted = Observable.FromEventPattern<BlockGraphEventArgs>(
                ev => _onRoundCompletedEventHandler += ev, ev => _onRoundCompletedEventHandler -= ev);
            _onRoundListener = OnRoundListener();
            Observable.Timer(TimeSpan.Zero, TimeSpan.FromHours(1)).Subscribe(_ =>
            {
                try
                {
                    var snapshot = _memStoreBlockGraph.GetMemSnapshot().SnapshotAsync().ToEnumerable();
                    var removeBlockGraphs =
                        snapshot.Where(x => x.Value.Block.Round < GetRound().GetAwaiter().GetResult());
                    foreach (var (key, _) in removeBlockGraphs)
                    {
                        _memStoreBlockGraph.Delete(key);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        public async Task<VerifyResult> NewBlockGraph(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                if (blockGraph.Block.Round != NextRound()) return VerifyResult.UnableToVerify;
                if (!_memStoreBlockGraph.Contains(blockGraph.ToIdentifier()))
                {
                    var finalized = await TryFinalize(blockGraph);
                    if (finalized != true)
                    {
                        return VerifyResult.UnableToVerify;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, ex.Message);
            }

            return VerifyResult.Succeed;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionId"></param>
        /// <returns></returns>
        public async Task<Transaction> Get(byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);
            Transaction transaction = null;
            try
            {
                var localNodeDetailsResponse = await _actorSystem.Root.RequestAsync<LocalNodeDetailsResponse>(_pidLocalNode,  new LocalNodeDetailsRequest());
                var snapshot = await _memStoreBlockGraph.GetMemSnapshot().SnapshotAsync().ToArrayAsync();
                var blocks = snapshot
                    .Where(x => x.Value.Block.Node == localNodeDetailsResponse.Identifier && x.Value.Block.Round == NextRound())
                    .Select(d => MessagePackSerializer.Deserialize<Block>(d.Value.Block.Data)).ToArray();
                foreach (var block in blocks)
                {
                    foreach (var tx in block.Txs)
                    {
                        if (tx.TxnId.Xor(transactionId))
                        {
                            transaction = tx;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to find transaction with {@txnId}", transactionId.ByteToHex());
            }

            return transaction;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        private void OnRoundReady(in BlockGraphEventArgs e)
        {
            if (e.BlockGraph.Block.Round == NextRound())
            {
                _onRoundCompletedEventHandler?.Invoke(this, e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private IDisposable OnRoundListener()
        {
            var onRoundCompletedSubscription = _onRoundCompleted
                .Where(data => data.EventArgs.BlockGraph.Block.Round == NextRound())
                .Throttle(TimeSpan.FromSeconds(OnRoundThrottleFromSeconds), NewThreadScheduler.Default).Subscribe(_ =>
                {
                    try
                    {
                        var snapshot = _memStoreBlockGraph.GetMemSnapshot().SnapshotAsync().ToEnumerable();
                        var blockGraphs = snapshot.Where(x => x.Value.Block.Round == NextRound()).ToArray();
                        if (blockGraphs.Length < 2) return;
                        var nodeCount = blockGraphs.Select(n => n.Value.Block.Node).Distinct().Count();
                        var f = (nodeCount - 1) / 3;
                        var quorum2F1 = 2 * f + 1;
                        if (nodeCount < quorum2F1) return;
                        var lastInterpreted = GetRound().GetAwaiter().GetResult();
                        var localNodeDetailsResponse = _actorSystem.Root
                            .RequestAsync<LocalNodeDetailsResponse>(_pidLocalNode, new LocalNodeDetailsRequest())
                            .GetAwaiter().GetResult();
                        var config = new Config(lastInterpreted, Array.Empty<ulong>(),
                            localNodeDetailsResponse.Identifier, (ulong)nodeCount);
                        var blockmania = new Blockmania(config, _logger) { NodeCount = nodeCount };
                        blockmania.TrackingDelivered.Subscribe(x =>
                        {
                            OnDeliveredReady(x.EventArgs.Interpreted).SafeFireAndForget();
                        });
                        var blockGraphTasks = new List<Task>();
                        blockGraphs.ForEach(next =>
                        {
                            var (_, blockGraph) = next;

                            async void Action()
                            {
                                await blockmania.Add(blockGraph, _applicationLifetime.ApplicationStopping);
                            }

                            var t = new Task(Action);
                            t.Start();
                            blockGraphTasks.Add(t);
                        });
                        Task.WhenAll(blockGraphTasks).Wait(_applicationLifetime.ApplicationStopping);
                    }
                    catch (Exception ex)
                    {
                        _logger.Here().Error(ex, "Process add blockmania error");
                    }
                }, exception => { _logger.Here().Error(exception, "Subscribe try add blockmania listener error"); });
            return onRoundCompletedSubscription;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<bool> Save(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                var verified = await _validator.VerifyBlockGraphSignatureNodeRound(blockGraph);
                if (verified == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Error("Unable to verify block for {@Node} and round {@Round}", blockGraph.Block.Node,
                        blockGraph.Block.Round);
                    _memStoreBlockGraph.Delete(blockGraph.ToIdentifier());
                    return false;
                }

                _memStoreBlockGraph.Put(blockGraph.ToIdentifier(), blockGraph);
            }
            catch (Exception)
            {
                _logger.Here().Error("Unable to save block for {@Node} and round {@Round}", blockGraph.Block.Node,
                    blockGraph.Block.Round);
            }

            return _memStoreBlockGraph.Contains(blockGraph.ToIdentifier());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<BlockGraph> Sign(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                var (signature, publicKey) = await _actorSystem.Root.RequestAsync<SignatureResponse>(_pidCryptoKeySign,
                    new SignatureRequest(CryptoKeySign.DefaultSigningKeyName, blockGraph.ToHash()));
                blockGraph.PublicKey = publicKey;
                blockGraph.Signature = signature;
                return blockGraph;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<BlockGraph> Copy(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                var localNodeDetailsResponse =
                    await _actorSystem.Root.RequestAsync<LocalNodeDetailsResponse>(_pidLocalNode,
                        new LocalNodeDetailsRequest());
                var copy = new BlockGraph
                {
                    Block = new Consensus.Models.Block
                    {
                        Data = blockGraph.Block.Data,
                        DataHash = blockGraph.Block.DataHash,
                        Hash = blockGraph.Block.Hash,
                        Node = localNodeDetailsResponse.Identifier,
                        Round = blockGraph.Block.Round
                    },
                    Prev = new Consensus.Models.Block
                    {
                        Data = blockGraph.Prev.Data,
                        DataHash = blockGraph.Prev.DataHash,
                        Hash = blockGraph.Prev.Hash,
                        Node = localNodeDetailsResponse.Identifier,
                        Round = blockGraph.Prev.Round
                    }
                };

                return copy;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<bool> TryFinalize(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                var localNodeDetailsResponse =
                    await _actorSystem.Root.RequestAsync<LocalNodeDetailsResponse>(_pidLocalNode,
                        new LocalNodeDetailsRequest());
                var copy = blockGraph.Block.Node != localNodeDetailsResponse.Identifier;
                if (copy)
                {
                    _logger.Here().Information("BlockGraph copy {@node}", blockGraph.Block.Node);
                    var saved = await Save(blockGraph);
                    if (saved == false) return false;
                    var copyBlockGraph = await Copy(blockGraph);
                    if (copyBlockGraph is null) return false;
                    var signBlockGraph = await Sign(copyBlockGraph);
                    if (signBlockGraph is null) return false;
                    var savedCopy = await Save(signBlockGraph);
                    if (savedCopy == false) return false;
                    _logger.Here().Information("BlockGraph copy BroadcastPeer");
                    await BroadcastPeers(signBlockGraph);
                    OnRoundReady(new BlockGraphEventArgs(blockGraph));
                }
                else
                {
                    _logger.Here().Information("BlockGraph self {@node}", blockGraph.Block.Node);
                    var signBlockGraph = await Sign(blockGraph);
                    if (signBlockGraph is null) return false;
                    var saved = await Save(signBlockGraph);
                    if (saved == false) return false;
                    _logger.Here().Information("BlockGraph self BroadcastPeers");
                    await BroadcastPeers(signBlockGraph);
                }
            }
            catch (Exception)
            {
                _logger.Here().Error("Unable to add block for {@Node} and round {@Round}", blockGraph.Block.Node,
                    blockGraph.Block.Round);
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="deliver"></param>
        /// <returns></returns>
        private async Task OnDeliveredReady(Interpreted deliver)
        {
            Guard.Argument(deliver, nameof(deliver)).NotNull();
            _logger.Here().Information("Delivered: {@Count} Consumed: {@Consumed} Round: {@Round}",
                deliver.Blocks.Count, deliver.Consumed, deliver.Round);
            foreach (var deliveredBlock in deliver.Blocks.Where(x => x.Data is { }).ToArray())
            {
                try
                {
                    if (deliveredBlock.Round != NextRound()) continue;
                    var block = MessagePackSerializer.Deserialize<Block>(deliveredBlock.Data);
                    _memStoreDelivered.Put(block.ToIdentifier(), block);
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }
            }

            await DecideWinner();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task DecideWinner()
        {
            (byte[] key, Block Value)[] deliveredBlocks = null;
            try
            {
                deliveredBlocks = await _memStoreDelivered.GetMemSnapshot().SnapshotAsync()
                    .Where(x => x.Value.Height == NextRound()).ToArrayAsync();
                if (deliveredBlocks.Any() != true) return;
                _logger.Here().Information("DecideWinnerAsync");
                var winners = deliveredBlocks.Where(x =>
                        x.Value.BlockPos.Solution == deliveredBlocks.Select(n => n.Value.BlockPos.Solution).Min())
                    .ToArray();
                _logger.Here().Information("Potential winners");
                foreach (var (_, winner) in winners)
                {
                    _logger.Here().Information("Hash {@Hash} Solution {@Sol}", winner.Hash.ByteToHex(),
                        winner.BlockPos.Solution);
                }

                (_, Block block) = winners.Length switch
                {
                    > 2 => winners.FirstOrDefault(winner =>
                        winner.Value.BlockPos.Solution >= deliveredBlocks.Select(x => x.Value.BlockPos.Solution).Max()),
                    _ => winners[0]
                };
                if (block is { })
                {
                    if (block.Height != NextRound()) return;
                    _logger.Here().Information("DecideWinnerAsync we have a winner {@Hash}", block.Hash.ByteToHex());
                    var blockExists = await _validator.BlockExists(block);
                    if (blockExists == VerifyResult.AlreadyExists)
                    {
                        _logger.Here().Error("Block winner already exists");
                        return;
                    }

                    var verifyBlockHeader = await _validator.VerifyBlock(block);
                    if (verifyBlockHeader == VerifyResult.UnableToVerify)
                    {
                        _logger.Here().Error("Unable to verify the block");
                        return;
                    }
                    
                    var saveBlockResponse =
                        await _actorSystem.Root.RequestAsync<SaveBlockResponse>(_pidShimCommand, new SaveBlockRequest(block));
                    if (!saveBlockResponse.OK)
                    {
                        _logger.Here().Error("Unable to save the block winner");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Decide winner failed");
            }
            finally
            {
                if (deliveredBlocks is { })
                    foreach (var deliveredBlock in deliveredBlocks)
                    {
                        _memStoreDelivered.Delete(deliveredBlock.key);
                    }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<ulong> GetRound()
        {
            var response = await _actorSystem.Root.RequestAsync<BlockHeightResponse>(_pidShimCommand,  new BlockHeightRequest());
            return (ulong)response.Count;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private ulong NextRound()
        {
            return GetRound().GetAwaiter().GetResult() + 1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task BroadcastPeers(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                if (blockGraph.Block.Round == NextRound())
                {
                    var gossipGraphResponse =
                        await _actorSystem.Root.RequestAsync<GossipGraphResponse>(_pidLocalNode,
                            new GossipGraphRequest());
                    var gossipGraph = gossipGraphResponse.GossipGraph;
                    var nodes = gossipGraph.Nodes.Where(x => x.State is MemberState.Alive or MemberState.Suspicious)
                        .ToArray();
                    var peers = nodes.Select(x => new Peer { Listening = x.Id.ToString() }).ToArray();
                    if (peers.Any())
                    {
                        peers.ForEach(x => x.BlockHeight = blockGraph.Block.Round);
                        _actorSystem.Root.Send(_pidLocalNode,
                            new BroadcastManualRequest(peers, TopicType.AddBlockGraph,
                                MessagePackSerializer.Serialize(blockGraph)));
                    }
                    else
                    {
                        _logger.Here().Fatal("Broadcast failed no peers");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Broadcast error");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _onRoundListener?.Dispose();
        }
    }
}
