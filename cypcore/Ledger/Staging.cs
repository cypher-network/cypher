// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Extentions;
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
            ILocalNode localNode, ILogger<Staging> logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _localNode = localNode;
            _logger = logger;

            _tcpSession = serfClient.TcpSessionsAddOrUpdate(new TcpSession(
                serfClient.SerfConfigurationOptions.Listening).Connect(serfClient.SerfConfigurationOptions.RPC));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task Ready(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(48);

            try
            {
                var memPools = await _unitOfWork.MemPoolRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(hash.ByteToHex()) && !string.IsNullOrEmpty(x.Block.PublicKey) && !string.IsNullOrEmpty(x.Block.Signature) && !x.Included));

                if (memPools.Any())
                {
                    var moreBlocks = await _unitOfWork.MemPoolRepository.MoreAsync(memPools);
                    var blockHashLookup = moreBlocks.ToLookup(i => i.Block.Hash);

                    await _unitOfWork.MemPoolRepository.IncludeAllAsync(memPools, _serfClient.ClientId);
                    await AddOrUpdate(blockHashLookup);
                }

                await Publish(hash);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< StagingProvider.Ready >>>: {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public async Task Publish(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(48);

            try
            {
                var memPool = await _unitOfWork.MemPoolRepository.LastOrDefaultAsync(x => new(x.Block.Hash.Equals(hash.ByteToHex())));
                if (memPool == null)
                {
                    return;
                }

                await _localNode.Broadcast(Helper.Util.SerializeProto(new List<MemPoolProto> { memPool }), TopicType.AddMemoryPool, "/pool");

                var staging = await _unitOfWork.StagingRepository.FirstOrDefaultAsync(x => new(x.Hash.Equals(memPool.Block.Hash)));
                if (staging != null)
                {
                    if (staging.Status != StagingState.Blockmainia
                        && staging.Status != StagingState.Running
                        && staging.Status != StagingState.Delivered)
                    {
                        staging.Status = StagingState.Pending;
                    }

                    var stored = await _unitOfWork.StagingRepository.PutAsync(staging, staging.ToIdentifier());
                    if (stored == null)
                    {
                        _logger.LogWarning($"Unable to mark the staging state as Dialling");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< PublishMemPoolProvider.Publish >>>: {ex}");
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

            StagingProto stageProto = null;
            int nodeCount = 0;

            try
            {
                var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                if (tcpSession.Ready)
                {
                    await _serfClient.Connect(tcpSession.SessionId);
                    var memberResult = await _serfClient.Members(tcpSession.SessionId);

                    nodeCount = memberResult.Success ? memberResult.Value.Members.Count(x => x.Status.Equals("alive")) : 0;
                }

                stageProto = new StagingProto
                {
                    Epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Hash = next.Block.Hash,
                    MemPoolProto = next,
                    ExpectedTotalNodes = 4,
                    Node = _serfClient.ClientId,
                    TotalNodes = nodeCount,
                    Status = StagingState.Started
                };

                stageProto.Nodes = new List<ulong>();
                stageProto.Nodes.AddRange(next.Deps?.Select(n => n.Block.Node));

                await AddWaitingOnRange(stageProto);

                stageProto.Status = Incoming(stageProto, next);

                ClearWaitingOn(stageProto);

                var saved = await _unitOfWork.StagingRepository.PutAsync(stageProto, stageProto.ToIdentifier());
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< StagingProvider.Add >>>: {ex}");
            }

            return stageProto;
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

            if (!membersResult.Success)
                return;

            stage.WaitingOn.AddRange(membersResult.Value.Members.Select(k => Helper.Util.HashToId(k.Tags["pubkey"])).ToArray());
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

            StagingProto staging = null;

            try
            {
                staging = await _unitOfWork.StagingRepository.FirstOrDefaultAsync(x => new(x.Hash.Equals(next.Block.Hash)));
                if (staging != null)
                {
                    staging.Nodes = new List<ulong>();
                    staging.Nodes.AddRange(next.Deps?.Select(n => n.Block.Node));

                    await AddWaitingOnRange(staging);

                    if (!staging.MemPoolProto.Equals(next))
                    {
                        staging.Status = Incoming(staging, next);

                        ClearWaitingOn(staging);
                    }

                    staging.MemPoolProto = next;

                    staging = await _unitOfWork.StagingRepository.PutAsync(staging, staging.ToIdentifier());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< StagingProvider.Existing >>>: {ex}");
            }

            return staging;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        /// <param name="staging"></param>
        public static StagingState Incoming(StagingProto staging, MemPoolProto next)
        {
            Guard.Argument(staging, nameof(staging)).NotNull();
            Guard.Argument(next, nameof(next)).NotNull();

            if (staging.Nodes.Any())
            {
                var nodes = staging.Nodes?.Except(next.Deps.Select(x => x.Block.Node));
                if (nodes.Any() != true)
                {
                    return StagingState.Blockmainia;
                }
            }

            if (staging.WaitingOn.Any())
            {
                var waitingOn = staging.WaitingOn?.Except(next.Deps.Select(x => x.Block.Node));
                if (waitingOn.Any() != true)
                {
                    return StagingState.Blockmainia;
                }
            }

            return staging.Status;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHashLookup"></param>
        /// <returns></returns>
        private async Task AddOrUpdate(ILookup<string, MemPoolProto> blockHashLookup)
        {
            Guard.Argument(blockHashLookup, nameof(blockHashLookup)).NotNull();

            if (blockHashLookup.Any() != true)
            {
                return;
            }

            foreach (var next in MemPoolProto.NextBlockGraph(blockHashLookup, _serfClient.ClientId))
            {
                var staging = await _unitOfWork.StagingRepository.FirstOrDefaultAsync(x => new(x.Hash.Equals(next.Block.Hash)));
                if (staging != null)
                {
                    if (staging.Status != StagingState.Blockmainia &&
                        staging.Status != StagingState.Running &&
                        staging.Status != StagingState.Delivered)
                    {
                        await Existing(next);
                    }

                    continue;
                }

                await Add(next);
            }
        }
    }
}
