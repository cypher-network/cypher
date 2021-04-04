// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        Task Ready();
        Task WriteAsync(int take, CancellationToken cancellationToken);
        Task<VerifyResult> TryAddBlockGraph(BlockGraph blockGraph);
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

        private Blockmania _blockmania;
        private Config _config;

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
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        public async Task<VerifyResult> TryAddBlockGraph(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            var exist = await BlockGraphExist(blockGraph);
            if (exist == VerifyResult.AlreadyExists)
            {
                _logger.Here()
                    .Error("Exists block {@Hash} for round {@Round} and node {@Node}", blockGraph.Block.Hash,
                        blockGraph.Block.Round, blockGraph.Block.Node);

                return exist;
            }

            await Task.Run(async () =>
                {
                    try
                    {
                        var copy = false;
                        copy |= blockGraph.Block.Node != _serfClient.ClientId;

                        if (!copy)
                        {
                            var signBlockGraph = await SignBlockGraph(blockGraph);
                            var saved = await SaveBlockGraph(signBlockGraph);
                            if (!saved) return VerifyResult.Invalid;

                            await Publish(signBlockGraph);
                        }
                        else
                        {
                            var saved = await SaveBlockGraph(blockGraph);
                            if (!saved) return VerifyResult.Invalid;

                            var block = Helper.Util.DeserializeFlatBuffer<BlockHeaderProto>(blockGraph.Block.Data);
                            var prev = Helper.Util.DeserializeFlatBuffer<BlockHeaderProto>(blockGraph.Prev.Data);

                            var copyBlockGraph = CopyBlockGraph(block, prev);
                            copyBlockGraph = await SignBlockGraph(copyBlockGraph);

                            var savedCopy = await SaveBlockGraph(copyBlockGraph);
                            if (!savedCopy) return VerifyResult.Invalid;

                            await Publish(copyBlockGraph);
                        }
                    }
                    catch (Exception)
                    {
                        _logger.Here()
                            .Error("Unable to add block for {@Node} and round {@Round}", blockGraph.Block.Node,
                                blockGraph.Block.Round);
                        return VerifyResult.Unknown;
                    }

                    return VerifyResult.Succeed;
                })
                .ConfigureAwait(false);

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Ready()
        {
            if (_sync.SyncRunning) return;

            var nodes = await _localNode.Nodes();
            if (nodes == null)
            {
                IsDebug();
                IsRelease();

                return;
            }

            if (_blockmania == null)
            {
                var lastInterpreted = await GetRound();

                _config = new Config(lastInterpreted, nodes, _serfClient.ClientId, (ulong)nodes.Length);
                _blockmania = new Blockmania(_config, _logger);
                _blockmania.Delivered += (sender, e) => Delivered(sender, e).SwallowException();
            }

            var blockInfos = _blockmania.Blocks.Where(x => !nodes.Contains(x.Data.Block.Node)).ToArray();
            foreach (var node in blockInfos.Select(x => x.Data.Block.Node))
            {
                if (_serfClient.ClientId == node) continue;

                var (_, peer) = (await _localNode.GetPeers()).FirstOrDefault(x => x.Key == node);

                try
                {
                    var networkBlockHeight = await _localNode.PeerBlockHeight(peer);
                    if (networkBlockHeight.Local.Height == networkBlockHeight.Remote.Height) continue;

                    var temp = new List<ulong>(nodes);
                    temp.Remove(node);
                    nodes = temp.ToArray();
                }
                catch (Exception)
                {
                    _logger.Here().Error("Unable to remove an unresponsive node {@NodeName}", peer.NodeName);
                }
            }

            var f = (nodes.Length - 1) / 3;

            _blockmania.NodeCount = nodes.Length;
            _blockmania.Nodes = nodes;
            _blockmania.Quorumf1 = f + 1;
            _blockmania.Quorum2f = 2 * f;
            _blockmania.Quorum2f1 = 2 * f + 1;

            _logger.Here()
                .Debug(
                    "Blockmania configuration: {@SelfId}, {@Round}, {@NodeCount}, {@Nodes}, {@TotalNodes}, {@f}, {@Quorumf1}, {@Quorum2f}, {@Quorum2f1}",
                    _blockmania.Self, _blockmania.Round, _blockmania.NodeCount, _blockmania.Nodes,
                    _blockmania.TotalNodes, f, _blockmania.Quorumf1, _blockmania.Quorum2f, _blockmania.Quorum2f1);
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
        [Conditional("DEBUG")]
        private void IsDebug()
        {
            _logger.Here().Error("Total number of nodes cannot be zero.");
        }

        /// <summary>
        /// 
        /// </summary>
        [Conditional("RELEASE")]
        private void IsRelease()
        {
            _logger.Here().Error("Total number of nodes cannot be zero.");
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
                blockHeaders = await _unitOfWork.DeliveredRepository.RangeAsync(skip, take);
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
                var height = await _unitOfWork.DeliveredRepository.CountAsync() - 147;
                height = height < 0 ? 0 : height;

                blockHeaders = await _unitOfWork.DeliveredRepository.RangeAsync(height, 147);
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
                height = await _unitOfWork.DeliveredRepository.CountAsync();
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
                var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x =>
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
        /// <param name="take"></param>
        /// <param name="cancellationToken"></param>
        public async Task WriteAsync(int take, CancellationToken cancellationToken)
        {
            Guard.Argument(take, nameof(take)).NotNegative();

            if (_blockmania == null) return;

            try
            {
                var blockGraphs = await _unitOfWork.BlockGraphRepository.TakeAsync(take);
                foreach (var blockGraph in blockGraphs.Where(blockGraph => _blockmania.Round <= blockGraph.Block.Round))
                {
                    _blockmania.Add(blockGraph);
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Queue exception");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="deliver"></param>
        /// <returns></returns>
        private async Task Delivered(object sender, Interpreted deliver)
        {
            Guard.Argument(deliver, nameof(deliver)).NotNull();

            try
            {
                foreach (var next in deliver.Blocks)
                {
                    if (next.Data == null) continue;

                    var blockGraph = await _unitOfWork.BlockGraphRepository.GetAsync(x =>
                        new ValueTask<bool>(x.Block.Hash.Equals(next.Hash) && x.Block.Round == next.Round));
                    if (blockGraph == null)
                    {
                        _logger.Here()
                            .Error("Unable to find the matching block - Hash: {@Hash} Round: {@Round} from node {@Node}",
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
                        await Process(blockHeader);
                        continue;
                    }

                    _logger.Here()
                        .Error("Unable to verify the node signatures - Hash: {@Hash} Round: {@Round} from node {@Node}",
                            next.Hash, next.Round, next.Node);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Blockmania error");
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
        private async Task<ulong> GetRound()
        {
            ulong round = 0;

            try
            {
                var height = await _unitOfWork.DeliveredRepository.CountAsync();
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
                await _localNode.Broadcast(Helper.Util.SerializeFlatBuffer(blockGraph), peers.Values.ToArray(),
                    TopicType.AddBlockGraph);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Publish error");
            }
        }
    }
}