// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        Task Ready(MemPoolProto memPool);
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
        /// <param name="memPool"></param>
        /// <returns></returns>
        public async Task Ready(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                var memPools = await _unitOfWork.MemPoolRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(memPool.Block.Hash)));

                if (memPools.Any())
                {
                    var memPoolList = new List<MemPoolProto>();
                    var memPoolsGroupBy = memPools.GroupBy(n => n.Block.Node, m => m,
                            (key, g) => new
                            {
                                Node = key,
                                memPools = g.DistinctBy(d => d.Block.Round).OrderBy(r => r.Block.Round).ToList()
                            })
                        .ToList();

                    var localMemPools = memPoolsGroupBy.Where(x => x.Node == _serfClient.ClientId).ToList();
                    foreach (var localMem in localMemPools)
                    {
                        for (var j = 0; j < localMem.memPools.Count; j++)
                        {
                            var next = localMem.memPools[j];

                            try
                            {
                                var previous = localMem.memPools[j - 1].Block;
                                next.Prev = previous;
                                next.Block.PreviousHash = previous.Hash;
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                            }

                            memPoolList.Add(next);
                        }
                    }

                    var remoteMemPools = memPoolsGroupBy.Where(x => x.Node != _serfClient.ClientId).ToList();
                    foreach (var remoteMem in remoteMemPools)
                    {
                        foreach (var next in remoteMem.memPools)
                        {
                            var nextDependency = memPoolList
                                .FirstOrDefault(x => x.Block.Round == next.Block.Round);

                            var depProto = new DepProto
                            {
                                Block = next.Block,
                                Deps = next.Deps.Select(x => new InterpretedProto()
                                {
                                    Hash = x.Block.Hash,
                                    InterpretedType = x.Block.InterpretedType,
                                    Node = x.Block.Node,
                                    PreviousHash = x.Block.PreviousHash,
                                    PublicKey = x.Block.PublicKey,
                                    Round = x.Block.Round,
                                    Signature = x.Block.Signature,
                                    Transaction = x.Block.Transaction
                                }).ToList(),
                                Prev = next.Prev
                            };

                            if (nextDependency is not null) nextDependency.Deps = new List<DepProto>() { depProto };
                        }
                    }

                    var list = memPoolList.ToArray();
                    _ = await IncludeMany(list);
                    _ = await AddOrUpdate(list, memPool.Block.Hash.HexToByte());
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
        private async Task<StagingProto> Add(MemPoolProto next)
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
                staging.MemPoolProtoList = new List<MemPoolProto> { next };
                staging.ExpectedTotalNodes = 4; // TODO: Should change in future when more rules apply.
                staging.Node = _serfClient.ClientId;
                staging.TotalNodes = nodeCount;
                staging.Status = StagingState.Started;

                staging.Nodes.AddRange(next.Deps?.Select(n => n.Block.Node) ?? Array.Empty<ulong>());

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
            stage.WaitingOn.AddRange(peers.Select(k => k.Value.ClientId));
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
        private async Task<StagingProto> Existing(MemPoolProto next)
        {
            Guard.Argument(next, nameof(next)).NotNull();
            StagingProto staging;

            try
            {
                staging = await _unitOfWork.StagingRepository.LastAsync(x =>
                    new ValueTask<bool>(x.Hash.Equals(next.Block.Hash)));

                if (staging != null)
                {
                    staging.Nodes = new List<ulong>();
                    staging.Nodes.AddRange(next.Deps?.Select(n => n.Block.Node) ?? Array.Empty<ulong>());

                    await AddWaitingOnRange(staging);

                    staging.Status = Incoming(staging, next);
                    staging.MemPoolProtoList.Add(next);

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
        private static StagingState Incoming(StagingProto staging, MemPoolProto next)
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
        /// <param name="memPools"></param>
        /// <param name="hash"></param>
        /// <returns></returns>
        private async Task<bool> AddOrUpdate(MemPoolProto[] memPools, byte[] hash)
        {
            Guard.Argument(memPools, nameof(memPools)).NotNull().NotEmpty();
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            StagingProto staging = null;

            try
            {
                foreach (var memPool in memPools)
                {
                    staging = await _unitOfWork.StagingRepository.LastAsync(x =>
                        new ValueTask<bool>(x.Hash.Equals(hash.ByteToHex())));

                    staging = staging != null ? await Existing(memPool) : await Add(memPool);
                }
            }
            catch (Exception ex)
            {
                staging = null;
                _logger.Here().Error(ex, "Cannot add to staging");
            }

            return staging != null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPools"></param>
        /// <returns></returns>
        private async Task<bool> IncludeMany(MemPoolProto[] memPools)
        {
            Guard.Argument(memPools, nameof(memPools)).NotNull().NotEmpty();

            var included = false;

            try
            {
                foreach (var next in memPools.Where(x => x.Block.Node == _serfClient.ClientId))
                {
                    included = await _unitOfWork.MemPoolRepository.PutAsync(next.ToIdentifier(), next);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while adding to memory pool");
            }

            return included;
        }
    }
}