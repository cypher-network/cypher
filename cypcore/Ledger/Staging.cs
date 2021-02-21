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
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using CYPCore.Network;

namespace CYPCore.Ledger
{
    public class Staging : IStaging
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;
        private readonly TcpSession _tcpSession;

        public Staging(IUnitOfWork unitOfWork, ISerfClient serfClient,
            ILocalNode localNode, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _localNode = localNode;
            _logger = logger.ForContext("SourceContext", nameof(Staging));

            _tcpSession = serfClient.TcpSessionsAddOrUpdate(new TcpSession(
                serfClient.SerfConfigurationOptions.Listening).Connect(serfClient.SerfConfigurationOptions.RPC));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public async Task Ready(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            try
            {
                var memPools = await _unitOfWork.MemPoolRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(hash.ByteToHex()) &&
                                        !string.IsNullOrEmpty(x.Block.PublicKey) &&
                                        !string.IsNullOrEmpty(x.Block.Signature) && !x.Included));
                if (memPools.Any())
                {
                    var moreBlocks = await _unitOfWork.MemPoolRepository.HasMoreAsync(memPools.ToArray());
                    var blockHashLookup = moreBlocks.ToLookup(i => i.Block.Hash);

                    blockHashLookup = blockHashLookup.Count switch
                    {
                        0 => memPools.ToLookup(i => i.Block.Hash),
                        _ => blockHashLookup
                    };

                    await _unitOfWork.MemPoolRepository.IncludeAsync(memPools.ToArray(), _serfClient.ClientId);
                    var added = await AddOrUpdate(blockHashLookup);
                    if (!added)
                    {
                        _logger.Here().Warning("Unable to publish hash: {@Hash}");
                        return;
                    }

                    await Publish(hash);
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
        /// <param name="hash"></param>
        /// <returns></returns>
        private async Task Publish(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);
            MemPoolProto memPool;

            try
            {
                memPool = await _unitOfWork.MemPoolRepository.LastAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(hash.ByteToHex()) &&
                                        x.Block.InterpretedType == InterpretedType.Pending && x.Included));
                if (memPool != null)
                {
                    var staging = await _unitOfWork.StagingRepository.LastAsync(x =>
                        new ValueTask<bool>(x.Hash.Equals(memPool.Block.Hash)));

                    if (staging != null)
                    {
                        if (staging.Status != StagingState.Blockmainia
                            && staging.Status != StagingState.Running
                            && staging.Status != StagingState.Delivered)
                        {
                            staging.Status = StagingState.Pending;
                        }

                        var saved = await _unitOfWork.StagingRepository.PutAsync(staging.ToIdentifier(), staging);
                        if (!saved)
                        {
                            _logger.Here().Warning($"Unable to mark the staging state as Dialling");
                            return;
                        }

                        await _localNode.Broadcast(Helper.Util.SerializeProto(memPool), TopicType.AddMemoryPool);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Publishing error");
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
            var nodeCount = 0;

            try
            {
                var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                if (tcpSession.Ready)
                {
                    await _serfClient.Connect(tcpSession.SessionId);
                    var memberResult = await _serfClient.Members(tcpSession.SessionId);

                    nodeCount = memberResult.Success
                        ? memberResult.Value.Members.Count(x => x.Status.Equals("alive"))
                        : 0;
                }

                staging = new StagingProto
                {
                    Epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Hash = next.Block.Hash,
                    MemPoolProto = next,
                    ExpectedTotalNodes = 4,
                    Node = _serfClient.ClientId,
                    TotalNodes = nodeCount,
                    Status = StagingState.Started,
                    Nodes = new List<ulong>()
                };

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

            var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
            var membersResult = await _serfClient.Members(tcpSession.SessionId);

            stage.WaitingOn = new List<ulong>();

            if (membersResult.Success)
            {
                stage.WaitingOn
                    .AddRange(membersResult.Value.Members.Select(k => Util.HashToId(k.Tags["pubkey"]))
                        .ToArray());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stage"></param>
        private static void ClearWaitingOn(StagingProto stage)
        {
            Guard.Argument(stage, nameof(stage)).NotNull();

            if (stage.Status == StagingState.Blockmainia || stage.Status == StagingState.Running)
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

                    if (!staging.MemPoolProto.Equals(next))
                    {
                        staging.Status = Incoming(staging, next);
                        ClearWaitingOn(staging);
                    }

                    staging.MemPoolProto = next;

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
                    return StagingState.Blockmainia;
                }
            }

            if (!staging.WaitingOn.Any()) return staging.Status;

            var waitingOn = staging.WaitingOn?.Except(next.Deps.Select(x => x.Block.Node));
            return waitingOn.Any() != true ? StagingState.Blockmainia : staging.Status;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHashLookup"></param>
        /// <returns></returns>
        private async Task<bool> AddOrUpdate(ILookup<string, MemPoolProto> blockHashLookup)
        {
            Guard.Argument(blockHashLookup, nameof(blockHashLookup)).NotNull();

            StagingProto staging = null;

            try
            {
                foreach (var next in MemPoolProto.NextBlockGraph(blockHashLookup, _serfClient.ClientId))
                {
                    staging = await _unitOfWork.StagingRepository.GetAsync(next.ToIdentifier());
                    if (staging != null)
                    {
                        if (staging.Status != StagingState.Blockmainia &&
                            staging.Status != StagingState.Running &&
                            staging.Status != StagingState.Delivered)
                        {
                            staging = await Existing(next);
                        }
                        continue;
                    }

                    staging = await Add(next);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot add to staging");
            }

            return staging != null;
        }
    }
}