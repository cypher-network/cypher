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

namespace CYPCore.Ledger
{
    public class MemoryPool : IMemoryPool
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly IValidator _validator;
        private readonly ISigning _signing;
        private readonly IStaging _staging;
        private readonly ILogger _logger;
        private readonly BackgroundQueue _queue;

        private TcpSession _tcpSession;
        private int _totalNodes;
        private Graph _graph;
        private Config _config;

        private LastInterpretedMessage _lastInterpretedMessage;

        public MemoryPool(IUnitOfWork unitOfWork, ISerfClient serfClient, IValidator validator,
            ISigning signing, IStaging staging, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _validator = validator;
            _signing = signing;
            _staging = staging;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPool));

            _queue = new();
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
                var exists = await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(memPool.Block.Hash) && x.Block.Node == memPool.Block.Node &&
                                        x.Block.Round == memPool.Block.Round));

                if (exists != null)
                {
                    _logger.Here().Error("Exists block {@Hash} for round {@Round} and node {@Node}",
                        memPool.Block.Hash,
                        memPool.Block.Round,
                        memPool.Block.Node);

                    return null;
                }

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

            await ReadySession();
            await _signing.GetOrUpsertKeyName(_signing.DefaultSigningKeyName);

            if (_graph == null)
            {
                var lastInterpreted = await LastInterpreted(hash);

                _config = new Config(lastInterpreted, new ulong[_totalNodes], _serfClient.ClientId,
                    (ulong)_totalNodes);
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
                await VerifySignatureAddToGraph(self);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task ReadySession()
        {
            _tcpSession = _serfClient
                .TcpSessionsAddOrUpdate(new TcpSession(_serfClient.SerfConfigurationOptions.Listening)
                    .Connect(_serfClient.SerfConfigurationOptions.RPC));

            var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);

            if (!tcpSession.Ready)
            {
                _logger.Here().Fatal("Serf client is not ready");
                return;
            }

            await _serfClient.Connect(tcpSession.SessionId);
            var countResult = await _serfClient.MembersCount(tcpSession.SessionId);

            if (!countResult.Success)
            {
                _logger.Here().Warning("{@Error}", ((SerfError)countResult.NonSuccessMessage).Error);
            }

            _totalNodes = countResult.Value;
            if (_totalNodes < 4)
            {
                _logger.Here().Warning("Minimum number of nodes required: 4. Total number of nodes {@TotalNodes}", _totalNodes);
            }

            if (_totalNodes == 0)
            {
                _logger.Here().Warning("Total number of nodes: {@TotalNodes}", _totalNodes);

                _totalNodes = 4;

                _logger.Here().Warning("Setting default number of nodes: {@TotalNodes}", _totalNodes);
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
                    var memPool = await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                        new ValueTask<bool>(x.Block.Hash.Equals(next.Hash) && x.Block.Node == next.Node &&
                                            x.Block.Round == next.Round));

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

                    var interpreted = new InterpretedProto
                    {
                        Hash = memPool.Block.Hash,
                        InterpretedType = InterpretedType.Pending,
                        Node = memPool.Block.Node,
                        PreviousHash = memPool.Block.PreviousHash,
                        PublicKey = memPool.Block.PublicKey,
                        Round = memPool.Block.Round,
                        Signature = memPool.Block.Signature,
                        Transaction = memPool.Block.Transaction
                    };

                    var hasSeen = await _unitOfWork.InterpretedRepository.FirstAsync(x =>
                        new ValueTask<bool>(x.Hash.Equals(interpreted.Hash)));

                    if (hasSeen != null)
                    {
                        _logger.Here().Error("Already seen interpreted block - Hash: {@Hash} Round: {@Round} from node {@Node}",
                            next.Hash,
                            next.Round,
                            next.Node);

                        continue;
                    }

                    var saved = await _unitOfWork.InterpretedRepository.PutAsync(interpreted.ToIdentifier(),
                        interpreted);
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
                ulong round = 0;

                round = await IncrementRound(memPool.Block.Hash.HexToByte(), memPool.Block.Round);
                if (round == 0)
                {
                    return null;
                }

                var copy = false;
                ulong node = 0;

                copy |= memPool.Block.Node != _serfClient.ClientId;
                node = copy ? _serfClient.ClientId : memPool.Block.Node;

                var prev = await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(memPool.Block.Hash.HexToByte()) && x.Block.Node == node &&
                                        x.Block.Round == round - 1));

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

            var signed = new MemPoolProto
            {
                Block = new InterpretedProto
                {
                    Hash = memPool.Block.Hash,
                    Node = node,
                    Round = round,
                    Transaction = memPool.Block.Transaction,
                    PublicKey = pubKey.ByteToHex(),
                    Signature = signature.ByteToHex()
                }
            };

            return signed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="round"></param>
        /// <returns></returns>
        private async Task<ulong> IncrementRound(byte[] hash, ulong round)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);

            ulong currentRound = 1;

            try
            {
                var memPoolProtos = await _unitOfWork.MemPoolRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Block.Hash.Equals(hash.ByteToHex())));

                if (memPoolProtos.Any())
                {
                    ulong[] order = new ulong[] { memPoolProtos.Last().Block.Round, round };
                    var isSequential = order.Zip(order.Skip(1), (a, b) => a + 1 == b).All(x => x);

                    currentRound = isSequential ? memPoolProtos.Last().Block.Round + 1 : 0;

                    if (currentRound == 0 && memPoolProtos.Count == 1)
                    {
                        currentRound = 1;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot increment round");
            }

            return currentRound;
        }
    }
}