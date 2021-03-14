// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CYPCore.Consensus.Models;
using CYPCore.Extensions;
using Dawn;
using Serilog;
using CYPCore.Extentions;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using CYPCore.Network;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IStaging
    {
        Task Ready(BlockGraph blockGraph);
    }

    /// <summary>
    /// 
    /// </summary>
    public class Staging : IStaging
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;

        public Staging(IUnitOfWork unitOfWork, ISerfClient serfClient, ILocalNode localNode, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _localNode = localNode;
            _logger = logger.ForContext("SourceContext", nameof(Staging));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        public async Task Ready(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            try
            {
                var blockGraphs = await _unitOfWork.BlockGraphRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(blockGraph.Block.Hash)));

                if (blockGraphs.Any())
                {
                    var blockGraphList = new List<BlockGraph>();
                    var blockGraphsGroupBy = blockGraphs.GroupBy(n => n.Block.Node, m => m,
                            (key, g) => new
                            {
                                Node = key,
                                blockGraphs = g.DistinctBy(d => d.Block.Round).OrderBy(r => r.Block.Round).ToList()
                            })
                        .ToList();

                    var localBlockGraphs = blockGraphsGroupBy.Where(x => x.Node == _serfClient.ClientId).ToList();
                    foreach (var localBlockGraph in localBlockGraphs)
                    {
                        for (var j = 0; j < localBlockGraph.blockGraphs.Count; j++)
                        {
                            var next = localBlockGraph.blockGraphs[j];

                            try
                            {
                                var previous = localBlockGraph.blockGraphs[j - 1].Block;
                                next.Prev = previous;
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                            }

                            blockGraphList.Add(next);
                        }
                    }

                    var remoteBlockGraphs = blockGraphsGroupBy.Where(x => x.Node != _serfClient.ClientId).ToList();
                    foreach (var dep in remoteBlockGraphs.SelectMany(remoteBlockGraph =>
                        remoteBlockGraph.blockGraphs.Select(nextRemote =>
                            new Dep(
                                new Block(nextRemote.Block.Hash, remoteBlockGraph.Node, nextRemote.Block.Round,
                                    nextRemote.Block.Data), nextRemote.Deps.Select(x => x.Block).ToList(),
                                nextRemote.Prev))))
                    {
                        blockGraphList.LastOrDefault()?.Deps.Add(dep);
                    }

                    _ = await AddOrUpdate(blockGraphList.ToArray(), blockGraph.Block.Hash.HexToByte());
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Staging error");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        /// <returns></returns>
        private async Task<StagingProto> Add(BlockGraph next)
        {
            Guard.Argument(next, nameof(next)).NotNull();

            StagingProto staging;

            try
            {
                var peers = await _localNode.GetPeers();
                var nodeCount = peers.Count;

                staging = StagingProto.CreateInstance();
                staging.Epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                staging.Hash = next.Block.Hash;
                staging.BlockGraphs = new List<BlockGraph> { next };
                staging.ExpectedTotalNodes = 4; // TODO: Should change in future when more rules apply.
                staging.Node = _serfClient.ClientId;
                staging.TotalNodes = nodeCount;
                staging.Status = StagingState.Started;

                ((List<ulong>)staging.Nodes).AddRange(next.Deps?.Select(n => n.Block.Node) ?? Array.Empty<ulong>());

                await AddWaitingOnRange(staging);

                staging.Status = Incoming(staging, next);

                ClearWaitingOn(staging);

                var saved = await _unitOfWork.StagingRepository.PutAsync(staging.ToIdentifier(), staging);
                if (!saved)
                {
                    _logger.Here().Warning("Unable to save staging with hash: {@Hash}",
                        staging.Hash);

                    staging = null;
                }
            }
            catch (Exception ex)
            {
                staging = null;
                _logger.Here().Error(ex, "Cannot add to staging");
            }

            return staging;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stage"></param>
        /// <returns></returns>
        private async Task AddWaitingOnRange(StagingProto stage)
        {
            Guard.Argument(stage, nameof(stage)).NotNull();

            var peers = await _localNode.GetPeers();
            ((List<ulong>)stage.WaitingOn).AddRange(peers.Select(k => k.Value.ClientId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stage"></param>
        private static void ClearWaitingOn(StagingProto stage)
        {
            Guard.Argument(stage, nameof(stage)).NotNull();

            if (stage.Status == StagingState.Blockmania || stage.Status == StagingState.Running)
            {
                stage.WaitingOn.Clear();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        /// <returns></returns>
        private async Task<StagingProto> Existing(BlockGraph next)
        {
            Guard.Argument(next, nameof(next)).NotNull();
            StagingProto staging;

            try
            {
                staging = await _unitOfWork.StagingRepository.GetAsync(x =>
                    new ValueTask<bool>(x.Hash.Equals(next.Block.Hash)));

                if (staging != null)
                {
                    staging.Nodes = new List<ulong>();
                    ((List<ulong>)staging.Nodes).AddRange(next.Deps?.Select(n => n.Block.Node) ?? Array.Empty<ulong>());

                    await AddWaitingOnRange(staging);

                    staging.Status = Incoming(staging, next);
                    staging.BlockGraphs.Add(next);

                    ClearWaitingOn(staging);

                    var saved = await _unitOfWork.StagingRepository.PutAsync(staging.ToIdentifier(), staging);
                    if (!saved)
                    {
                        _logger.Here().Warning("Unable to save staging with hash: {@Hash}",
                            staging.Hash);

                        staging = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot add to staging");
                staging = null;
            }

            return staging;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        /// <param name="staging"></param>
        private static StagingState Incoming(StagingProto staging, BlockGraph next)
        {
            Guard.Argument(staging, nameof(staging)).NotNull();
            Guard.Argument(next, nameof(next)).NotNull();

            if (staging.Nodes.Any())
            {
                var nodes = staging.Nodes?.Except(next.Deps.Select(x => x.Block.Node)).ToList();
                if (nodes.Any() != true)
                {
                    return StagingState.Blockmania;
                }
            }

            if (!staging.WaitingOn.Any()) return staging.Status;

            var waitingOn = staging.WaitingOn?.Except(next.Deps.Select(x => x.Block.Node));
            return waitingOn.Any() != true ? StagingState.Blockmania : staging.Status;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraphs"></param>
        /// <param name="hash"></param>
        /// <returns></returns>
        private async Task<VerifyResult> AddOrUpdate(BlockGraph[] blockGraphs, byte[] hash)
        {
            Guard.Argument(blockGraphs, nameof(blockGraphs)).NotNull().NotEmpty();
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            StagingProto staging = null;

            try
            {
                foreach (var blockGraph in blockGraphs)
                {
                    staging = await _unitOfWork.StagingRepository.GetAsync(x =>
                        new ValueTask<bool>(x.Hash.Equals(hash.ByteToHex())));

                    staging = staging != null ? await Existing(blockGraph) : await Add(blockGraph);
                }
            }
            catch (Exception ex)
            {
                staging = null;
                _logger.Here().Error(ex, "Cannot add to staging");
            }

            return staging != null ? VerifyResult.Succeed : VerifyResult.Invalid;
        }
    }
}