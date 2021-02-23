// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;

using Dawn;
using Serilog;

using CYPCore.Consensus.Blockmania;
using CYPCore.Extentions;
using CYPCore.Messages;
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
        Task Ready(byte[] hash);
    }

    /// <summary>
    /// 
    /// </summary>
    public class MemoryPool : IMemoryPool
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly IValidator _validator;
        private readonly ISigning _signing;
        private readonly IStaging _staging;
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;
        private readonly BackgroundQueue _queue;

        private Graph _graph;
        private Config _config;

        private LastInterpretedMessage _lastInterpretedMessage;

        public MemoryPool(IUnitOfWork unitOfWork, ISerfClient serfClient, IValidator validator,
            ISigning signing, IStaging staging, ILocalNode localNode, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _validator = validator;
            _signing = signing;
            _staging = staging;
            _localNode = localNode;
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
                    var saved = await _unitOfWork.MemPoolRepository.PutAsync(memPool.ToIdentifier(), memPool);
                    if (!saved)
                    {
                        _logger.Here().Error("Unable to save block {@Hash} for round {@Round} and node {@Node}",
                            memPool.Block.Hash,
                            memPool.Block.Round,
                            memPool.Block.Node);

                        return null;
                    }

                    await _queue.QueueTask(() => Ready(memPool.Block.Hash.HexToByte()));
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
        /// <param name="hash"></param>
        /// <returns></returns>
        public async Task Ready(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            var peers = await _localNode.GetPeers();
            var totalNodes = peers.Count;

            if (totalNodes < 4)
            {
                _logger.Here().Warning("Minimum number of nodes required: 4. Total number of nodes {@TotalNodes}", totalNodes);
            }

            if (totalNodes == 0)
            {
                _logger.Here().Warning("Total number of nodes: {@TotalNodes}", totalNodes);
                totalNodes = 4;
                _logger.Here().Warning("Setting default number of nodes: {@TotalNodes}", totalNodes);
            }

            await _signing.GetOrUpsertKeyName(_signing.DefaultSigningKeyName);

            if (_graph == null)
            {
                var lastInterpreted = await LastInterpreted(hash);

                _config = new Config(lastInterpreted, new ulong[totalNodes], _serfClient.ClientId, (ulong)totalNodes);

                _graph = new Graph(_config);
                _graph.BlockmaniaInterpreted += (sender, e) => BlockmaniaCallback(sender, e).SwallowException();
            }

            var memPools = await _unitOfWork.MemPoolRepository.WhereAsync(x =>
                new ValueTask<bool>(x.Block.Hash.Equals(hash.ByteToHex()) && !x.Included && !x.Replied));

            foreach (var memPool in memPools)
            {
                var self = await UpsertSelf(memPool);
                if (self == null)
                {
                    _logger.Here().Error("Unable to set own block Hash: {@Hash} Round: {@Round} from node {@Node}",
                        memPool.Block.Hash,
                        memPool.Block.Round,
                        memPool.Block.Node);

                    continue;
                }

                await _staging.Ready(hash);
                _ = await VerifySignatureAddToGraph(self);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<bool> VerifySignatureAddToGraph(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                var staging = await _unitOfWork.StagingRepository.FirstAsync(x =>
                    new ValueTask<bool>(x.Hash.Equals(memPool.Block.Hash) && x.Status == StagingState.Blockmainia));

                if (staging != null)
                {
                    var verified = await _validator.VerifyMemPoolSignatures(memPool);
                    if (verified == false)
                    {
                        return false;
                    }

                    _graph.Add(staging.MemPoolProto.ToMemPool());

                    staging.Status = StagingState.Running;

                    var saved = await _unitOfWork.StagingRepository.PutAsync(staging.ToIdentifier(), staging);
                    if (!saved)
                    {
                        _logger.Here().Warning("Unable to save staging with hash: {@Hash}", staging.Hash);
                        staging = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify signature");
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        private async Task<ulong> LastInterpreted(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            InterpretedProto interpreted = null;

            try
            {
                interpreted = await _unitOfWork.InterpretedRepository.LastAsync(x =>
                    new ValueTask<bool>(x.Hash.Equals(hash.ByteToHex())));
            }
            catch (Exception ex)
            {
                _logger.Here().Warning(ex, "Cannot get element");
            }
            finally
            {
                _lastInterpretedMessage = interpreted switch
                {
                    null => new LastInterpretedMessage(0, null),
                    _ => new LastInterpretedMessage(interpreted.Round, interpreted),
                };
            }

            return _lastInterpretedMessage.Last > 0 ? _lastInterpretedMessage.Last - 1 : _lastInterpretedMessage.Last;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockmaniaInterpreted"></param>
        private async Task BlockmaniaCallback(object sender, Interpreted blockmaniaInterpreted)
        {
            Guard.Argument(blockmaniaInterpreted, nameof(blockmaniaInterpreted)).NotNull();

            try
            {
                foreach (var next in blockmaniaInterpreted.Blocks)
                {
                    var hasSeen = await _unitOfWork.InterpretedRepository.FirstAsync(x =>
                        new ValueTask<bool>(x.Hash.Equals(next.Hash)));

                    if (hasSeen != null)
                    {
                        var staging = await _unitOfWork.StagingRepository.FirstAsync(x =>
                            new ValueTask<bool>(x.Hash.Equals(next.Hash)));

                        if (staging != null)
                        {
                            staging.Status = StagingState.Delivered;
                            var savedStaging = await _unitOfWork.StagingRepository.PutAsync(staging.ToIdentifier(), staging);
                            if (savedStaging)
                            {
                                _logger.Here().Information(
                                    "Marked staging state as Delivered - Hash: {@Hash} Round: {@Round} from Node {@Node}",
                                    next.Hash,
                                    next.Round,
                                    next.Node);
                            }
                            else
                            {
                                _logger.Here().Warning("Unable to mark the staging state as Delivered");
                            }
                        }
                        else
                        {
                            _logger.Here().Error("Unable to find matching block - Hash: {@Hash} Round: {@Round} from node {@Node}",
                                next.Hash,
                                next.Round,
                                next.Node);
                        }

                        continue;
                    }

                    var memPool = await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                        new ValueTask<bool>(
                            x.Block.Hash.Equals(next.Hash) &&
                            x.Block.Node == next.Node &&
                            x.Block.Round == next.Round &&
                            !string.IsNullOrEmpty(x.Block.PublicKey) &&
                            !string.IsNullOrEmpty(x.Block.Signature)));

                    if (memPool == null)
                    {
                        _logger.Here().Error("Unable to find matching block - Hash: {@Hash} Round: {@Round} from node {@Node}",
                            next.Hash,
                            next.Round,
                            next.Node);

                        continue;
                    }

                    var verified = await _validator.VerifyMemPoolSignatures(memPool);
                    if (verified == false)
                    {
                        _logger.Here().Error("Unable to verify node signatures - Hash: {@Hash} Round: {@Round} from node {@Node}",
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

                    var saved = await _unitOfWork.InterpretedRepository.PutAsync(interpreted.ToIdentifier(), interpreted);
                    if (!saved)
                    {
                        _logger.Here().Error("Unable to save block for {@Node} and round {@Round}",
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
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<MemPoolProto> UpsertSelf(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            MemPoolProto stored = null;

            try
            {
                var copy = false;
                copy |= memPool.Block.Node != _serfClient.ClientId;

                ulong round = 0, node = 0;

                switch (copy)
                {
                    case false:
                        node = memPool.Block.Node;
                        round = memPool.Block.Round;
                        break;
                    case true:
                        round = await IncrementRound(memPool.Block.Hash.HexToByte());
                        if (round == 0)
                        {
                            return null;
                        }
                        node = _serfClient.ClientId;
                        break;
                }

                var prev = await _unitOfWork.MemPoolRepository.PreviousAsync(memPool.Block.Hash.HexToByte(), node, round);
                if (prev != null)
                {
                    memPool.Block.PreviousHash = prev.Block.Hash;

                    if (prev.Block.Round + 1 != memPool.Block.Round)
                        memPool.Prev = prev.Block;
                }

                var signed = await Sign(node, round, memPool);
                var saved = await _unitOfWork.MemPoolRepository.PutAsync(signed.ToIdentifier(), signed);
                if (!saved)
                {
                    _logger.Here().Error("Unable to save block for {@Node} and round {@Round}",
                        memPool.Block.Node,
                        memPool.Block.Round);

                    return null;
                }

                stored = signed;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while upserting");
            }

            return stored;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="round"></param>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<MemPoolProto> Sign(ulong node, ulong round, MemPoolProto memPool)
        {
            Guard.Argument(node, nameof(node)).NotNegative();
            Guard.Argument(round, nameof(round)).NotNegative();
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            var signature = await _signing.Sign(_signing.DefaultSigningKeyName, memPool.Block.Transaction.ToHash());
            var pubKey = await _signing.GetPublicKey(_signing.DefaultSigningKeyName);

            var signed = MemPoolProto.CreateInstance();
            signed.Block = InterpretedProto.CreateInstance();
            signed.Block.Hash = memPool.Block.Hash;
            signed.Block.Node = node;
            signed.Block.Round = round;
            signed.Block.Transaction = memPool.Block.Transaction;
            signed.Block.PublicKey = pubKey.ByteToHex();
            signed.Block.Signature = signature.ByteToHex();

            return signed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        private async Task<ulong> IncrementRound(byte[] hash)
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
                var exists = await _unitOfWork.DeliveredRepository
                    .FirstAsync(x =>
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