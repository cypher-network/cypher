// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dawn;
using Serilog;

using CYPCore.Extentions;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Network;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IMemoryPool
    {
        Task<MemPoolProto> AddTransaction(MemPoolProto memPool);
    }

    /// <summary>
    /// 
    /// </summary>
    public class MemoryPool : IMemoryPool
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signing;
        private readonly IStaging _staging;
        private readonly ILocalNode _localNode;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly BackgroundQueue _queue;

        public MemoryPool(IUnitOfWork unitOfWork, ISerfClient serfClient,
            ISigning signing, IStaging staging, ILocalNode localNode, IValidator validator, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _signing = signing;
            _staging = staging;
            _localNode = localNode;
            _validator = validator;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPool));

            _queue = new BackgroundQueue();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        public async Task<MemPoolProto> AddTransaction(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                var memExists = await TransactionMemoryPoolExist(memPool);
                var delivered = await TransactionDeliveredExist(memPool.Block.Transaction);

                if (!memExists && !delivered)
                {
                    var pool = memPool;
                    await Task.Run(async () =>
                    {
                        var self = await SignOrVerifyThenSave(pool);
                        if (self == null) return;

                        var nextRound = false;
                        nextRound |= pool.Block.Node != _serfClient.ClientId;
                        if (nextRound)
                        {
                            self = await NextRoundThenSignOrVerifyThenSave(pool);
                            if (self == null) return;
                        }

                        await _staging.Ready(self);
                        await Publish(self);

                    }).ConfigureAwait(false);
                }
                else
                {
                    _logger.Here().Error("Exists block {@Hash} for round {@Round} and node {@Node}",
                        memPool.Block.Hash,
                        memPool.Block.Round,
                        memPool.Block.Node);

                    return null;
                }
            }
            catch (Exception ex)
            {
                memPool = null;
                _logger.Here().Error(ex, "Cannot add transaction");
            }

            return memPool;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task Publish(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                var staging = await _unitOfWork.StagingRepository.LastAsync(x =>
                    new ValueTask<bool>(x.Hash.Equals(memPool.Block.Hash)));

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
                    foreach (var round in matrix.Sending)
                    {
                        var memPoolCheck =
                            await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                                new ValueTask<bool>(x.Block.Hash.Equals(memPool.Block.Hash) &&
                                                    x.Block.Node == _serfClient.ClientId &&
                                                    x.Block.Round == (ulong)round));

                        if (memPoolCheck == null) continue;

                        tasks.Add(_localNode.Broadcast(Util.SerializeProto(memPool), new[] { matrix.Peer },
                            TopicType.AddMemoryPool));
                    }
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
        private static void Matrix(Dictionary<ulong, Peer> peers, IStagingProto staging, out List<BroadcastMatrix> listMatrix)
        {
            Guard.Argument(peers, nameof(peers)).NotNull();
            Guard.Argument(staging, nameof(staging)).NotNull();

            listMatrix = new List<BroadcastMatrix>();
            foreach (var bMatrix in peers.Select(peer => new BroadcastMatrix { Peer = peer.Value }))
            {
                bMatrix.Received = staging.MemPoolProtoList.SelectMany(x => x.Deps)
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<MemPoolProto> NextRoundThenSignOrVerifyThenSave(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            var series = Enumerable.Range(0, 2);
            var stores = await Task.WhenAll(series.Select(async _ =>
            {
                var mem = memPool.Cast();

                mem.Block.Round = await NextRound(mem.Block.Hash.HexToByte());
                mem.Block.Node = _serfClient.ClientId;
                mem.Deps = new List<DepProto>();
                mem.Prev = InterpretedProto.CreateInstance();

                return await SignOrVerifyThenSave(mem);
            }));

            return stores.Last();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<MemPoolProto> SignOrVerifyThenSave(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                if (memPool.Block.Node == _serfClient.ClientId)
                {
                    await _signing.GetOrUpsertKeyName(_signing.DefaultSigningKeyName);

                    var signature = await _signing.Sign(_signing.DefaultSigningKeyName, memPool.Block.Transaction.ToHash());
                    var pubKey = await _signing.GetPublicKey(_signing.DefaultSigningKeyName);

                    memPool.Block.PublicKey = pubKey.ByteToHex();
                    memPool.Block.Signature = signature.ByteToHex();
                }
                else
                {
                    var valid = await _validator.VerifyMemPoolSignature(memPool);
                    if (!valid)
                    {
                        _logger.Here().Error("Unable to verify block for {@Node} and round {@Round}",
                            memPool.Block.Node,
                            memPool.Block.Round);

                        return null;
                    }
                }

                var saved = await _unitOfWork.MemPoolRepository.PutAsync(memPool.ToIdentifier(), memPool);
                if (saved) return memPool;

                _logger.Here().Error("Unable to save block for {@Node} and round {@Round}",
                    memPool.Block.Node,
                    memPool.Block.Round);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to validate/sign and save");
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        private async Task<ulong> NextRound(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            ulong round = 0;

            try
            {
                var memPool = await _unitOfWork.MemPoolRepository.LastAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(hash.ByteToHex()) &&
                                        x.Block.Node == _serfClient.ClientId));
                if (memPool == null)
                {
                    round++;
                }
                else
                {
                    round = memPool.Block.Round + 1;
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
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<bool> TransactionMemoryPoolExist(IMemPoolProto memPool)
        {
            var exist = await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                new ValueTask<bool>(x.Block.Hash.Equals(memPool.Block.Hash) && x.Block.Node == memPool.Block.Node &&
                                    x.Block.Round == memPool.Block.Round));
            return exist != null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<bool> TransactionDeliveredExist(ITransactionProto tx)
        {
            foreach (var vin in tx.Vin)
            {
                var exists = await _unitOfWork.DeliveredRepository.FirstAsync(x =>
                    new ValueTask<bool>(x.Transactions.Any(t => t.Vin.First().Key.K_Image.Xor(vin.Key.K_Image))));

                if (exists != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}