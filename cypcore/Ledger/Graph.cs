// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
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
using FlatSharp;
using Serilog;

namespace CYPCore.Ledger
{
    public interface IGraph
    {
        Task Ready();
        Task WriteAsync(int take, CancellationToken cancellationToken);
        void StopWriter();
        Task<VerifyResult> TryAddBlockGraph(BlockGraph blockGraph);
        Task<VerifyResult> AddBlock(BlockHeaderProto payload);
        Task AddBlocks(BlockHeaderProto[] payloads);
        Task<VoutProto[]> GetTransaction(byte[] txnId);
        Task<IEnumerable<BlockHeaderProto>> GetBlockHeaders(int skip, int take);
        Task<IEnumerable<BlockHeaderProto>> GetSafeguardBlocks();
        Task<long> GetHeight();
    }

    public class Graph : IGraph
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IStaging _staging;
        private readonly ILocalNode _localNode;
        private readonly ISerfClient _serfClient;
        private readonly IValidator _validator;
        private readonly ISigning _signing;
        private readonly ILogger _logger;
        private readonly ChannelWriter<BlockGraph> _writer;
        private readonly ChannelReader<BlockGraph> _reader;

        private Blockmania _blockmania;
        private Config _config;

        public Graph(IUnitOfWork unitOfWork, IStaging staging, ILocalNode localNode,
            ISerfClient serfClient, IValidator validator, ISigning signing, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _staging = staging;
            _localNode = localNode;
            _serfClient = serfClient;
            _validator = validator;
            _signing = signing;
            _logger = logger;

            var channel = Channel.CreateUnbounded<BlockGraph>();
            _reader = channel.Reader;
            _writer = channel.Writer;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        public async Task<VerifyResult> TryAddBlockGraph(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            try
            {
                var exist = await BlockGraphExist(blockGraph);
                if (exist == VerifyResult.AlreadyExists)
                {
                    _logger.Here().Error("Exists block {@Hash} for round {@Round} and node {@Node}",
                        blockGraph.Block.Hash,
                        blockGraph.Block.Round,
                        blockGraph.Block.Node);

                    return exist;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, ex.Message);
                return VerifyResult.Invalid;
            }

            PrepareBroadcasting(blockGraph);

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Ready()
        {
            var totalNodes = await GetTotalNodes();
            if (totalNodes == null)
            {
                return;
            }

            if (_blockmania == null)
            {
                var lastInterpreted = await LastInterpreted();

                _config = new Config(lastInterpreted, totalNodes, _serfClient.ClientId, (ulong)totalNodes.Length);
                _blockmania = new Blockmania(_config, _logger);
                _blockmania.Delivered += (sender, e) => Delivered(sender, e).SwallowException();

                await WaitForReader(2);
            }
            else
            {
                _blockmania.NodeCount = totalNodes.Length;
                _blockmania.Nodes = totalNodes;

                _logger.Here().Debug("Blockmania configuration: {@SelfId}, {@Round}, {@NodeCount}, {@Nodes}, {@TotalNodes}",
                    _blockmania.Self, _blockmania.Round, _blockmania.NodeCount, _blockmania.Nodes, _blockmania.TotalNodes);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<ulong[]> GetTotalNodes()
        {
            var peers = await _localNode.GetPeers();
            var totalNodes = (ulong)(peers?.Count ?? 0);
            if (totalNodes != 0) return peers?.Keys.ToArray();

            IsDebug();
            IsRelease();

            return null;
        }

        [Conditional("DEBUG")]
        private void IsDebug()
        {
            _logger.Here().Error("Total number of nodes cannot be zero.");
        }

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
        public async Task<IEnumerable<BlockHeaderProto>> GetBlockHeaders(int skip, int take)
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
                _logger.Here().Error(ex, "Cannot get block headers");
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
                var count = await _unitOfWork.DeliveredRepository.CountAsync();
                var last = await _unitOfWork.DeliveredRepository.LastAsync();

                if (last != null)
                {
                    var height = last.Height - count;

                    height = height < 0 ? 0 : height;

                    blockHeaders = await _unitOfWork.DeliveredRepository.RangeAsync(height, 147);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get safeguard blocks");
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
                var last = await _unitOfWork.DeliveredRepository.LastAsync();
                if (last != null)
                {
                    height = last.Height == 0 ? 1 : last.Height + 1;
                }
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
                _logger.Here().Error(ex, "Cannot get Vout");
            }

            return outputs;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        public async Task<VerifyResult> AddBlock(BlockHeaderProto blockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();

            try
            {
                var processed = await Process(blockHeader);
                if (processed == VerifyResult.Invalid)
                {
                    _logger.Here().Error("Unable to process the block header");
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot add block");
            }

            return VerifyResult.Invalid;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeaders"></param>
        /// <returns></returns>
        public async Task AddBlocks(BlockHeaderProto[] blockHeaders)
        {
            Guard.Argument(blockHeaders, nameof(blockHeaders)).NotNull().NotEmpty();

            try
            {
                foreach (var blockHeader in blockHeaders)
                {
                    var processed = await Process(blockHeader);
                    if (processed == VerifyResult.Succeed) continue;

                    _logger.Here().Error("Unable to process the block header");
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot add block");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        private void PrepareBroadcasting(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            var graph = blockGraph;
            Task.Run(async () =>
            {
                var signOrVerifyThenSave = await SignOrVerifyThenSave(graph);
                if (signOrVerifyThenSave == VerifyResult.UnableToVerify) return;

                var nextRound = false;
                nextRound |= graph.Block.Node != _serfClient.ClientId;
                if (nextRound)
                {
                    var g = graph.Cast();

                    g.Block.Round = await NextRound(g);
                    g.Block.Node = _serfClient.ClientId;
                    g.Deps = new List<Dep>();
                    g.Prev = new Block();

                    var signOrVerifyThenSaveNextRound = await SignOrVerifyThenSave(g);
                    if (signOrVerifyThenSaveNextRound == VerifyResult.UnableToVerify) return;
                }

                await _staging.Ready(graph);
                await Publish(graph);

            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        private async Task<VerifyResult> Process(BlockHeaderProto blockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();

            var exists = await Exists(blockHeader);
            if (exists == VerifyResult.AlreadyExists) return VerifyResult.Invalid;

            var verifyBlockHeader = await _validator.VerifyBlockHeader(blockHeader);
            if (verifyBlockHeader == VerifyResult.UnableToVerify)
            {
                _logger.Here().Error("Unable to verify block header");
                return VerifyResult.Invalid;
            }

            var saved = await _unitOfWork.DeliveredRepository.PutAsync(blockHeader.ToIdentifier(), blockHeader);
            if (saved) return VerifyResult.Succeed;

            _logger.Here().Error("Unable to save block header: {@MerkleRoot}", blockHeader.MerkelRoot);

            return VerifyResult.Invalid;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="take"></param>
        /// <param name="cancellationToken"></param>
        public async Task WriteAsync(int take, CancellationToken cancellationToken)
        {
            Guard.Argument(take, nameof(take)).NotNegative();

            try
            {
                var staging = await _unitOfWork.StagingRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Status == StagingState.Blockmania));

                foreach (var staged in staging.Take(take))
                {
                    foreach (var blockGraph in staged.BlockGraphs)
                    {
                        await _writer.WriteAsync(blockGraph, cancellationToken);
                    }

                    staged.Status = StagingState.Running;

                    var saved = await _unitOfWork.StagingRepository.PutAsync(staged.ToIdentifier(), staged);
                    if (saved) return;

                    _logger.Here().Warning("Unable to save staging with hash: {@Hash}", staged.Hash);
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
        public void StopWriter()
        {
            _writer.Complete();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="threads"></param>
        /// <returns></returns>
        private async Task WaitForReader(int threads)
        {
            Guard.Argument(threads, nameof(threads)).NotNegative();

            if (_blockmania == null)
            {
                return;
            }

            for (var i = 0; i < threads; i++)
            {
                await Task.Factory.StartNew(async () =>
                {
                    while (await _reader.WaitToReadAsync())
                        while (_reader.TryRead(out var blockGraph))
                        {
                            var hasInfo = _blockmania.Blocks.FirstOrDefault(x =>
                                x.Data.Block.Hash.Equals(blockGraph.Block.Hash) &&
                                x.Data.Block.Node == blockGraph.Block.Node && x.Data.Block.Round == blockGraph.Block.Round);

                            if (hasInfo != null) continue;

                            _blockmania.Add(blockGraph);

                            await Task.Delay(100);
                            await RemoveAndUpdate(blockGraph.Block.Hash.HexToByte(), StagingState.Dequeued);
                        }
                }, TaskCreationOptions.LongRunning);
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

                    var blockHeader = FlatBufferSerializer.Default.Parse<BlockHeaderProto>(next.Data);
                    var exists = await Exists(blockHeader);
                    if (exists == VerifyResult.AlreadyExists) continue;

                    var blockGraph = await _unitOfWork.BlockGraphRepository.GetAsync(x =>
                        new ValueTask<bool>(
                            x.Block.Hash.Equals(next.Hash) &&
                            x.Block.Node == next.Node &&
                            x.Block.Round == next.Round));

                    if (blockGraph == null)
                    {
                        _logger.Here().Error(
                            "Unable to find matching block - Hash: {@Hash} Round: {@Round} from node {@Node}",
                            next.Hash,
                            next.Round,
                            next.Node);

                        continue;
                    }

                    var verifyBlockGraphSignatureNodeRound = await _validator.VerifyBlockGraphSignatureNodeRound(blockGraph);
                    if (verifyBlockGraphSignatureNodeRound == VerifyResult.Succeed)
                    {
                        await Process(blockHeader);
                        continue;
                    }

                    _logger.Here().Error(
                        "Unable to verify node signatures - Hash: {@Hash} Round: {@Round} from node {@Node}",
                        next.Hash,
                        next.Round,
                        next.Node);
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
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        private async Task<VerifyResult> Exists(BlockHeaderProto blockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();

            var hasSeen = await _unitOfWork.DeliveredRepository.GetAsync(x =>
                new ValueTask<bool>(x.ToIdentifier().Xor(blockHeader.ToIdentifier())));

            return hasSeen != null ? VerifyResult.AlreadyExists : VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="stagingState"></param>
        /// <returns></returns>
        private async Task<bool> RemoveAndUpdate(byte[] hash, StagingState stagingState)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            var staging = await _unitOfWork.StagingRepository.WhereAsync(x =>
                new ValueTask<bool>(x.Hash.Equals(hash.ByteToHex())));

            if (staging.Any())
            {
                foreach (var staged in staging)
                {
                    var removed = await _unitOfWork.StagingRepository.RemoveAsync(staged.ToIdentifier());
                    if (!removed)
                    {
                        _logger.Here().Warning("Unable to remove staging - Hash: {@Hash}", staged.Hash);
                    }

                    staged.Status = stagingState;
                    var savedStaging = await _unitOfWork.StagingRepository.PutAsync(staged.ToIdentifier(), staged);
                    if (savedStaging)
                    {
                        _logger.Here().Information(
                            "Marked staging state as " + stagingState + " - Hash: {@Hash} from Node {@Node}",
                            staged.Hash,
                            staged.Node);
                    }
                    else
                    {
                        _logger.Here().Warning("Unable to mark the staging state as " + stagingState);
                    }
                }
            }
            else
            {
                _logger.Here().Error("Unable to find matching block - Hash: {@hash}",
                    hash.ByteToHex());
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<ulong> LastInterpreted()
        {
            ulong round = 0;

            try
            {
                var delivered = await _unitOfWork.DeliveredRepository.LastAsync();
                if (delivered != null)
                {
                    round = (ulong)delivered.Height;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Warning(ex, "Cannot get element");
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

            var exist = await _unitOfWork.BlockGraphRepository.GetAsync(x =>
                new ValueTask<bool>(x.Block.Hash.Equals(blockGraph.Block.Hash) &&
                                    x.Block.Node == blockGraph.Block.Node &&
                                    x.Block.Round == blockGraph.Block.Round));

            return exist == null ? VerifyResult.Unknown : VerifyResult.AlreadyExists;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<VerifyResult> SignOrVerifyThenSave(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            try
            {
                if (blockGraph.Block.Node == _serfClient.ClientId)
                {
                    await _signing.GetOrUpsertKeyName(_signing.DefaultSigningKeyName);

                    var signature = await _signing.Sign(_signing.DefaultSigningKeyName, blockGraph.ToHash());
                    var pubKey = await _signing.GetPublicKey(_signing.DefaultSigningKeyName);

                    blockGraph.PublicKey = pubKey;
                    blockGraph.Signature = signature;
                }
                else
                {
                    var verified = await _validator.VerifyBlockGraphSignatureNodeRound(blockGraph);
                    if (verified != VerifyResult.Succeed)
                    {
                        _logger.Here().Error("Unable to verify block for {@Node} and round {@Round}",
                            blockGraph.Block.Node,
                            blockGraph.Block.Round);

                        return VerifyResult.UnableToVerify;
                    }
                }

                var saved = await _unitOfWork.BlockGraphRepository.PutAsync(blockGraph.ToIdentifier(), blockGraph);
                if (saved) return VerifyResult.Succeed;

                _logger.Here().Error("Unable to save block for {@Node} and round {@Round}",
                    blockGraph.Block.Node,
                    blockGraph.Block.Round);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to validate/sign and save");
            }

            return VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        private async Task<ulong> NextRound(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            ulong round = 0;

            try
            {
                var blockGraphs = await _unitOfWork.BlockGraphRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(blockGraph.Block.Hash) && x.Block.Node == blockGraph.Block.Node));

                if (blockGraphs.Any())
                {
                    round = blockGraphs.OrderBy(r => r.Block.Round).LastOrDefault().Block.Round + 1;
                }
                else
                {
                    round++;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot increment round");
            }

            return round;
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
                var staging = await _unitOfWork.StagingRepository.GetAsync(x =>
                    new ValueTask<bool>(x.Hash.Equals(blockGraph.Block.Hash)));

                if (staging == null) return;

                if (staging.Status != StagingState.Blockmania
                    && staging.Status != StagingState.Running
                    && staging.Status != StagingState.Delivered)
                {
                    staging.Status = StagingState.Pending;

                    var saved = await _unitOfWork.StagingRepository.PutAsync(staging.ToIdentifier(), staging);
                    if (!saved)
                    {
                        _logger.Here().Warning($"Unable to mark the staging state as Pending");
                    }
                }

                var peers = await _localNode.GetPeers();

                Matrix(peers, staging, out var listMatrix);

                var tasks = new List<Task>();
                foreach (var matrix in listMatrix)
                {
                    var doesExist =
                        await _unitOfWork.BlockGraphRepository.GetAsync(x =>
                            new ValueTask<bool>(x.Block.Hash.Equals(blockGraph.Block.Hash) &&
                                                x.Block.Node == _serfClient.ClientId &&
                                                x.Block.Round == (ulong)matrix.Sending.Last()));

                    if (doesExist == null) continue;

                    var maxBytesNeeded = FlatBufferSerializer.Default.GetMaxSize(doesExist);
                    var buffer = new byte[maxBytesNeeded];

                    FlatBufferSerializer.Default.Serialize(doesExist, buffer);

                    tasks.Add(_localNode.Broadcast(buffer, new[] { matrix.Peer },
                        TopicType.AddBlockGraph));
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Publishing error");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="peers"></param>
        /// <param name="staging"></param>
        /// <param name="listMatrix"></param>
        private static void Matrix(Dictionary<ulong, Peer> peers, IStagingProto staging,
            out List<BroadcastMatrix> listMatrix)
        {
            Guard.Argument(peers, nameof(peers)).NotNull();
            Guard.Argument(staging, nameof(staging)).NotNull();

            listMatrix = new List<BroadcastMatrix>();
            foreach (var bMatrix in peers.Select(peer => new BroadcastMatrix { Peer = peer.Value }))
            {
                bMatrix.Received = staging.BlockGraphs.SelectMany(x => x.Deps)
                    .Where(x => x.Block.Node == bMatrix.Peer.ClientId)
                    .Select(x => (int)x.Block.Round)
                    .ToArray();

                if (bMatrix.Received.Any() != true)
                {
                    bMatrix.Received = new[] { 0 };
                }

                bMatrix.Sending = new int[bMatrix.Received.Length];

                for (var i = 0; i < bMatrix.Received.Length; i++)
                {
                    bMatrix.Sending[i] = bMatrix.Received[i] + 1;
                }

                listMatrix.Add(bMatrix);
            }
        }
    }
}