// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Collections.Pooled;
using CYPCore.Consensus;
using CYPCore.Consensus.Models;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Extentions;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Persistence;
using CYPCore.Serf;
using Dawn;
using Serilog;

namespace CYPCore.Ledger
{
    public interface IGraph
    {
        Task<VerifyResult> TryAddBlockGraph(BlockGraph blockGraph);
        Task<VerifyResult> TryAddBlockGraph(byte[] blockGraphModel);
        Task<VoutProto[]> GetTransaction(byte[] txnId);
        Task<IEnumerable<BlockHeaderProto>> GetBlocks(int skip, int take);
        Task<IEnumerable<BlockHeaderProto>> GetSafeguardBlocks();
        Task<long> GetHeight();
    }

    public class Graph : IGraph
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILocalNode _localNode;
        private readonly ISerfClient _serfClient;
        private readonly IValidator _validator;
        private readonly ISigning _signing;
        private readonly ISync _sync;
        private readonly ILogger _logger;
        private readonly PooledList<BlockGraph> _pooledBlockGraphs;
        private readonly IObservable<EventPattern<BlockGraphEventArgs>> _trackingBlockGraphAdded;
        private readonly IObservable<EventPattern<BlockGraphEventArgs>> _trackingBlockGraphCompleted;

        private const int MaxBlockGraphs = 10_000;

        protected class BlockGraphEventArgs : EventArgs
        {
            public BlockGraph BlockGraph { get; }
            public string Hash { get; }

            public BlockGraphEventArgs(BlockGraph blockGraph)
            {
                BlockGraph = blockGraph;
                Hash = blockGraph.Block.Hash;
            }
        }

        private EventHandler<BlockGraphEventArgs> _blockGraphAddedEventHandler;
        private EventHandler<BlockGraphEventArgs> _blockGraphAddCompletedEventHandler;

        public Graph(IUnitOfWork unitOfWork, ILocalNode localNode, ISerfClient serfClient, IValidator validator,
            ISigning signing, ISync sync, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _localNode = localNode;
            _serfClient = serfClient;
            _validator = validator;
            _signing = signing;
            _sync = sync;
            _logger = logger;
            _pooledBlockGraphs = new PooledList<BlockGraph>(MaxBlockGraphs);

            _trackingBlockGraphAdded = Observable.FromEventPattern<BlockGraphEventArgs>(
                ev => _blockGraphAddedEventHandler += ev, ev => _blockGraphAddedEventHandler -= ev);
            _trackingBlockGraphCompleted = Observable.FromEventPattern<BlockGraphEventArgs>(
                ev => _blockGraphAddCompletedEventHandler += ev, ev => _blockGraphAddCompletedEventHandler -= ev);

            TryAddBlockGraphsListener();
            TryAddBlockmaniaListener();
            ReplayLastRound().SafeFireAndForget(exception => { _logger.Here().Error(exception, "Replay error"); });
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task ReplayLastRound()
        {
            var blockGraphs =
                await _unitOfWork.BlockGraphRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Block.Round == GetRound() + 1));
            foreach (var blockGraph in blockGraphs)
            {
                OnBlockGraphAddComplete(new BlockGraphEventArgs(blockGraph));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnBlockGraphAdd(BlockGraphEventArgs e)
        {
            var handler = _blockGraphAddedEventHandler;
            handler?.Invoke(this, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnBlockGraphAddComplete(BlockGraphEventArgs e)
        {
            var handler = _blockGraphAddCompletedEventHandler;
            handler?.Invoke(this, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraphModel"></param>
        /// <returns></returns>
        public async Task<VerifyResult> TryAddBlockGraph(byte[] blockGraphModel)
        {
            var blockGraph = Helper.Util.DeserializeFlatBuffer<BlockGraph>(blockGraphModel);
            await TryAddBlockGraph(blockGraph);

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        public Task<VerifyResult> TryAddBlockGraph(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            try
            {
                if (_pooledBlockGraphs.Contains(blockGraph))
                {
                    return Task.FromResult(VerifyResult.AlreadyExists);
                }

                _pooledBlockGraphs.Add(blockGraph);
                OnBlockGraphAdd(new BlockGraphEventArgs(blockGraph));
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, ex.Message);
                return Task.FromResult(VerifyResult.Invalid);
            }

            return Task.FromResult(VerifyResult.Succeed);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<bool> SaveBlockGraph(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            var verified = await _validator.VerifyBlockGraphSignatureNodeRound(blockGraph);
            if (verified == VerifyResult.UnableToVerify)
            {
                _logger.Here()
                    .Error("Unable to verify block for {@Node} and round {@Round}", blockGraph.Block.Node,
                        blockGraph.Block.Round);
                return false;
            }

            var saved = await _unitOfWork.BlockGraphRepository.PutAsync(blockGraph.ToIdentifier(), blockGraph);
            if (saved) return true;

            _logger.Here()
                .Error("Unable to save block for {@Node} and round {@Round}", blockGraph.Block.Node,
                    blockGraph.Block.Round);

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<BlockGraph> SignBlockGraph(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            await _signing.GetOrUpsertKeyName(_signing.DefaultSigningKeyName);

            var signature = await _signing.Sign(_signing.DefaultSigningKeyName, blockGraph.ToHash());
            var pubKey = await _signing.GetPublicKey(_signing.DefaultSigningKeyName);

            blockGraph.PublicKey = pubKey;
            blockGraph.Signature = signature;

            return blockGraph;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<IEnumerable<BlockHeaderProto>> GetBlocks(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            var blockHeaders = Enumerable.Empty<BlockHeaderProto>();

            try
            {
                blockHeaders = await _unitOfWork.HashChainRepository.RangeAsync(skip, take);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the blocks");
            }

            return blockHeaders;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<BlockHeaderProto>> GetSafeguardBlocks()
        {
            var blockHeaders = Enumerable.Empty<BlockHeaderProto>();

            try
            {
                var height = await _unitOfWork.HashChainRepository.CountAsync() - 147;
                height = height < 0 ? 0 : height;

                blockHeaders = await _unitOfWork.HashChainRepository.RangeAsync(height, 147);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the safeguard blocks");
            }

            return blockHeaders;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<long> GetHeight()
        {
            long height = 0;

            try
            {
                height = await _unitOfWork.HashChainRepository.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get block height");
            }

            return height;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionId"></param>
        /// <returns></returns>
        public async Task<VoutProto[]> GetTransaction(byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);

            VoutProto[] outputs = null;

            try
            {
                var blockHeaders = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Transactions.Any(t => t.TxnId.Xor(transactionId))));
                var firstBlockHeader = blockHeaders.FirstOrDefault();
                var found = firstBlockHeader?.Transactions.FirstOrDefault(x => x.TxnId.Xor(transactionId));
                if (found != null)
                {
                    outputs = found.Vout;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable tp get outputs");
            }

            return outputs;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <param name="prevBlockHeader"></param>
        /// <returns></returns>
        private BlockGraph CopyBlockGraph(BlockHeaderProto blockHeader, BlockHeaderProto prevBlockHeader)
        {
            var blockGraph = new BlockGraph
            {
                Block = new Block(blockHeader.MerkelRoot, _serfClient.ClientId,
                    (ulong)blockHeader.Height, Helper.Util.SerializeFlatBuffer(blockHeader)),
                Prev = new Block
                {
                    Data = Helper.Util.SerializeFlatBuffer(prevBlockHeader),
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
        /// <returns></returns>s
        private IDisposable TryAddBlockGraphsListener()
        {
            var activityTrackSubscription = _trackingBlockGraphAdded
                .Where(data => data.EventArgs.BlockGraph.Block.Round == GetRound() + 1)
                .GroupByUntil(item => item.EventArgs.Hash, g => g.Throttle(TimeSpan.FromSeconds(10)).Take(1))
                .SelectMany(group => group.Buffer(TimeSpan.FromSeconds(1), 500))
                .Subscribe(async blockGraphs =>
                {
                    foreach (var data in blockGraphs)
                    {
                        var blockGraph = data.EventArgs.BlockGraph;

                        try
                        {
                            var copy = false;

                            copy |= blockGraph.Block.Node != _serfClient.ClientId;

                            if (!copy)
                            {
                                var signBlockGraph = await SignBlockGraph(blockGraph);
                                var saved = await SaveBlockGraph(signBlockGraph);
                                if (!saved) return;

                                await Publish(signBlockGraph);
                            }
                            else
                            {
                                var saved = await SaveBlockGraph(blockGraph);
                                if (!saved) return;

                                var block = Helper.Util.DeserializeFlatBuffer<BlockHeaderProto>(blockGraph.Block.Data);
                                var prev = Helper.Util.DeserializeFlatBuffer<BlockHeaderProto>(blockGraph.Prev.Data);

                                var copyBlockGraph = CopyBlockGraph(block, prev);
                                copyBlockGraph = await SignBlockGraph(copyBlockGraph);

                                var savedCopy = await SaveBlockGraph(copyBlockGraph);
                                if (!savedCopy) return;

                                await Publish(copyBlockGraph);
                            }

                            OnBlockGraphAddComplete(new BlockGraphEventArgs(blockGraph));
                        }
                        catch (Exception)
                        {
                            _logger.Here()
                                .Error("Unable to add block for {@Node} and round {@Round}", blockGraph.Block.Node,
                                    blockGraph.Block.Round);
                        }
                    }
                });

            return Task.FromResult(activityTrackSubscription);
        }

        /// <summary>
        /// 
        /// </summary>
        private IDisposable TryAddBlockmaniaListener()
        {
            var activityTrackSubscription = _trackingBlockGraphCompleted
                .Delay(TimeSpan.FromSeconds(10))
                .GroupBy(g => g.EventArgs.Hash)
                .SelectMany(blockGraph => Observable.FromAsync(async () => await _unitOfWork.BlockGraphRepository.WhereAsync(
                    x =>
                        new ValueTask<bool>(x.Block.Round == GetRound() + 1 && x.Block.Hash.Equals(blockGraph.Key)))))
                .Subscribe(blockgraphs =>
                {
                    try
                    {
                        var nodeCount = blockgraphs.Select(n => n.Block.Node).Count();
                        var nodes = blockgraphs.Select(n => n.Block.Node).ToArray();
                        var f = (nodeCount - 1) / 3;
                        var quorum2F1 = 2 * f + 1;

                        if (nodeCount < quorum2F1) return;

                        var lastInterpreted = GetRound();

                        var config = new Config(lastInterpreted, nodes, _serfClient.ClientId, (ulong)nodeCount);
                        var blockmania = new Blockmania(config, _logger);

                        blockmania.TrackingDelivered.Subscribe(x =>
                        {
                            Delivered(x.EventArgs.Interpreted).ConfigureAwait(false);
                        });

                        foreach (var next in blockgraphs)
                        {
                            blockmania.Add(next);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Here().Error(ex, "Process add blockmania error");
                    }
                });

            return activityTrackSubscription;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="deliver"></param>
        /// <returns></returns>
        private async Task Delivered(Interpreted deliver)
        {
            Guard.Argument(deliver, nameof(deliver)).NotNull();

            _logger.Here().Information("Delivered");

            try
            {
                foreach (var next in deliver.Blocks.Where(x => x.Data != null))
                {
                    var blockGraph = await _unitOfWork.BlockGraphRepository.GetAsync(x =>
                        new ValueTask<bool>(x.Block.Hash.Equals(next.Hash) && x.Block.Round == next.Round));
                    if (blockGraph == null)
                    {
                        _logger.Here()
                            .Error(
                                "Unable to find the matching block - Hash: {@Hash} Round: {@Round} from node {@Node}",
                                next.Hash, next.Round, next.Node);
                        continue;
                    }

                    await RemoveBlockGraph(blockGraph, next);

                    var block = Helper.Util.DeserializeFlatBuffer<BlockHeaderProto>(next.Data);
                    var exists = await _validator.BlockExists(block);
                    if (exists == VerifyResult.AlreadyExists)
                    {
                        continue;
                    }

                    var verifyBlockGraphSignatureNodeRound =
                        await _validator.VerifyBlockGraphSignatureNodeRound(blockGraph);
                    if (verifyBlockGraphSignatureNodeRound == VerifyResult.Succeed)
                    {
                        var saved = await _unitOfWork.DeliveredRepository.PutAsync(block.ToIdentifier(), block);
                        if (!saved)
                        {
                            _logger.Here().Error("Unable to save the block: {@MerkleRoot}", block.MerkelRoot);
                        }

                        _logger.Here().Information("Saved block to Delivered");

                        continue;
                    }

                    _logger.Here()
                        .Error("Unable to verify the node signatures - Hash: {@Hash} Round: {@Round} from node {@Node}",
                            next.Hash, next.Round, next.Node);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Delivered error");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        private async Task RemoveBlockGraph(BlockGraph blockGraph, Block next)
        {
            _pooledBlockGraphs.Remove(blockGraph);

            var removed = await _unitOfWork.BlockGraphRepository.RemoveAsync(blockGraph.ToIdentifier());
            if (!removed)
            {
                _logger.Here()
                    .Error(
                        "Unable to remove the block graph for block - Hash: {@Hash} Round: {@Round} from node {@Node}",
                        next.Hash, next.Round, next.Node);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private ulong GetRound()
        {
            var round = GetRoundAsync().ConfigureAwait(false);
            return round.GetAwaiter().GetResult();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<ulong> GetRoundAsync()
        {
            ulong round = 0;

            try
            {
                var height = await _unitOfWork.HashChainRepository.CountAsync();
                round = (ulong)height - 1;
            }
            catch (Exception ex)
            {
                _logger.Here().Warning(ex, "Unable to get the round");
            }

            return round;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<VerifyResult> BlockGraphExist(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            BlockGraph existBlockGraph;

            try
            {
                existBlockGraph = await _unitOfWork.BlockGraphRepository.GetAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(blockGraph.Block.Hash) &&
                                        x.Block.Node == blockGraph.Block.Node &&
                                        x.Block.Round == blockGraph.Block.Round));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            return existBlockGraph == null ? VerifyResult.Unknown : VerifyResult.AlreadyExists;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task Publish(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            try
            {
                var peers = await _localNode.GetPeers();
                await _localNode.Broadcast(peers.Values.ToArray(), TopicType.AddBlockGraph,
                    Helper.Util.SerializeFlatBuffer(blockGraph));
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Publish error");
            }
        }
    }
}