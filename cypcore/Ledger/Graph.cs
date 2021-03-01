// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CYPCore.Consensus;
using CYPCore.Consensus.Models;
using CYPCore.Extensions;
using CYPCore.Extentions;
using CYPCore.Helper;
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
        Task Ready(int threads);
        void WriteAsync(int count, CancellationToken cancellationToken);
        void StopWriter();
    }

    public class Graph : IGraph
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILocalNode _localNode;
        private readonly ISerfClient _serfClient;
        private readonly IValidator _validator;
        private readonly ILogger _logger;

        private Blockmania _blockmania;
        private Config _config;

        private ChannelWriter<MemPoolProto> _writer;

        public Graph(IUnitOfWork unitOfWork, ILocalNode localNode,
            ISerfClient serfClient, IValidator validator, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _localNode = localNode;
            _serfClient = serfClient;
            _validator = validator;
            _logger = logger;
        }

        public async Task Ready(int threads)
        {
            var peers = await _localNode.GetPeers();
            var totalNodes = peers?.Count ?? 0;
            if (totalNodes == 0)
            {
                _logger.Here().Warning("Total number of nodes: {@TotalNodes}", totalNodes);
                totalNodes = 1;
                _logger.Here().Warning("Setting default number of nodes: {@TotalNodes}", totalNodes);
            }

            if (_blockmania == null)
            {
                var lastInterpreted = await LastInterpreted();

                _config = new Config(lastInterpreted, new ulong[totalNodes], _serfClient.ClientId, (ulong)totalNodes);

                _blockmania = new Blockmania(_config);
                _blockmania.Delivered += (sender, e) => Delivered(sender, e).SwallowException();
            }

            await WaitForReader(threads);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="threads"></param>
        /// <returns></returns>
        private async Task WaitForReader(int threads)
        {
            var channel = Channel.CreateUnbounded<MemPoolProto>();
            var reader = channel.Reader;
            _writer = channel.Writer;

            for (var i = 0; i < threads; i++)
            {
                await Task.Factory.StartNew(async () =>
                {
                    while (await reader.WaitToReadAsync())
                    {
                        var memPool = await reader.ReadAsync();

                        _blockmania.Add(memPool.ToBlockGraph());
                        await RemoveAndUpdate(memPool.Block.Hash.HexToByte(), StagingState.Dequeued);
                    }
                }, TaskCreationOptions.LongRunning);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="count"></param>
        /// <param name="cancellationToken"></param>
        public void WriteAsync(int count, CancellationToken cancellationToken)
        {
            var task = Task.Factory.StartNew(async () =>
            {
                try
                {
                    var staging = await _unitOfWork.StagingRepository.WhereAsync(x =>
                        new ValueTask<bool>(x.Status == StagingState.Blockmania));

                    foreach (var staged in staging.Take(count))
                    {
                        foreach (var memPool in staged.MemPoolProtoList)
                        {
                            await _writer.WriteAsync(memPool, cancellationToken);
                        }

                        staged.Status = StagingState.Running;

                        var saved = await _unitOfWork.StagingRepository.PutAsync(staged.ToIdentifier(), staged);
                        if (saved) return;

                        _logger.Here().Warning("Unable to save staging with hash: {@Hash}", staged.Hash);
                    }
                }
                catch (QueueException ex)
                {
                    _logger.Here().Error(ex, "Queue exception");
                }
            }, cancellationToken);

            task.Wait(cancellationToken);
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
                    if (await SeenBefore(next)) continue;

                    var memPool = await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                        new ValueTask<bool>(
                            x.Block.Hash.Equals(next.Hash) &&
                            x.Block.Node == next.Node &&
                            x.Block.Round == next.Round));

                    if (memPool == null)
                    {
                        _logger.Here().Error(
                            "Unable to find matching block - Hash: {@Hash} Round: {@Round} from node {@Node}",
                            next.Hash,
                            next.Round,
                            next.Node);

                        continue;
                    }

                    var verified = await _validator.VerifyMemPoolSignature(memPool);
                    if (verified == false)
                    {
                        _logger.Here().Error(
                            "Unable to verify node signatures - Hash: {@Hash} Round: {@Round} from node {@Node}",
                            next.Hash,
                            next.Round,
                            next.Node);

                        continue;
                    }

                    var interpreted = InterpretedProto.CreateInstance();
                    interpreted.Hash = memPool.Block.Hash;
                    interpreted.InterpretedType = InterpretedType.Pending;
                    interpreted.Node = memPool.Block.Node;
                    interpreted.PreviousHash = memPool.Block.PreviousHash;
                    interpreted.PublicKey = memPool.Block.PublicKey;
                    interpreted.Round = memPool.Block.Round;
                    interpreted.Signature = memPool.Block.Signature;
                    interpreted.Transaction = memPool.Block.Transaction;

                    var saved = await _unitOfWork.InterpretedRepository.PutAsync(interpreted.ToIdentifier(),
                        interpreted);

                    if (!saved)
                    {
                        _logger.Here().Error("Unable to save block for {@Node} and round {@Round}",
                            interpreted.Node,
                            interpreted.Round);
                    }

                    var removed = await RemoveAndUpdate(next.Hash.HexToByte(), StagingState.Delivered);
                    if (!removed)
                    {
                        _logger.Here().Error("Unable to remove and update block for {@Node} and round {@Round}",
                            interpreted.Node,
                            interpreted.Round);
                    }
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
        /// <param name="next"></param>
        /// <returns></returns>
        private async Task<bool> SeenBefore(BlockId next)
        {
            var hasSeen = await _unitOfWork.InterpretedRepository.FirstAsync(x =>
                new ValueTask<bool>(x.Hash.Equals(next.Hash)));

            if (hasSeen == null) return false;
            {
                return await RemoveAndUpdate(next.Hash.HexToByte(), StagingState.Delivered);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="stagingState"></param>
        /// <returns></returns>
        private async Task<bool> RemoveAndUpdate(byte[] hash, StagingState stagingState)
        {
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
                            "Marked staging state as Delivered - Hash: {@Hash} from Node {@Node}",
                            staged.Hash,
                            staged.Node);
                    }
                    else
                    {
                        _logger.Here().Warning("Unable to mark the staging state as Delivered");
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
                var interpreted = await _unitOfWork.InterpretedRepository.LastAsync();
                if (interpreted != null)
                {
                    round = interpreted.Round > 0 ? interpreted.Round - 1 : interpreted.Round;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Warning(ex, "Cannot get element");
            }

            return round;
        }
    }
}