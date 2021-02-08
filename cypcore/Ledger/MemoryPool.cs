// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Consensus.Blockmania;
using CYPCore.Extentions;
using CYPCore.Messages;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf;
using CYPCore.Cryptography;
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
        private Graph Graph;
        private Config Config;

        private LastInterpretedMessage _lastInterpretedMessage;

        public MemoryPool(IUnitOfWork unitOfWork, ISerfClient serfClient, IValidator validator,
            ISigning signing, IStaging staging, ILogger<MemoryPool> logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _validator = validator;
            _signing = signing;
            _staging = staging;
            _logger = logger;

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

            MemPoolProto stored = null;

            try
            {
                var exists = await _unitOfWork.MemPoolRepository
                    .FirstOrDefaultAsync(x => new(x.Block.Hash.Equals(memPool.Block.Hash) && x.Block.Node == memPool.Block.Node && x.Block.Round == memPool.Block.Round));

                if (exists != null)
                {
                    _logger.LogError($"<<< MemoryPool.AddMemPoolTransaction >>>: Exists block {memPool.Block.Hash} for round {memPool.Block.Round} and node {memPool.Block.Node}");
                    return null;
                }

                stored = await _unitOfWork.MemPoolRepository.PutAsync(memPool, memPool.ToIdentifier());
                if (stored == null)
                {
                    _logger.LogError($"<<< MemoryPool.AddMemPoolTransaction >>>: Unable to save block {memPool.Block.Hash} for round {memPool.Block.Round} and node {memPool.Block.Node}");
                    return null;
                }

                await _queue.QueueTask(() =>
                {
                    return Ready(memPool.Block.Hash.HexToByte());
                });

            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPool.AddTransaction >>>: {ex}");
            }

            return stored;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public async Task Ready(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(48);

            await ReadySession();
            await _signing.GetOrUpsertKeyName(_signing.DefaultSigningKeyName);

            if (Graph == null)
            {
                var lastInterpreted = await LastInterpreted(hash);

                Config = new Config(lastInterpreted, new ulong[_totalNodes], _serfClient.ClientId, (ulong)_totalNodes);
                Graph = new Graph(Config);

                Graph.BlockmaniaInterpreted += (sender, e) => BlockmaniaCallback(sender, e).SwallowException();
            }

            var memPools = await _unitOfWork.MemPoolRepository.WhereAsync(x => new ValueTask<bool>(x.Block.Hash == hash.ByteToHex() && !x.Included && !x.Replied));
            foreach (var memPool in memPools)
            {
                var self = await UpsertSelf(memPool);
                if (self == null)
                {
                    _logger.LogError($"<<< MemoryPool.UpsertSelf >>>: " +
                        $"Unable to set own block Hash: {memPool.Block.Hash} Round: {memPool.Block.Round} from node {memPool.Block.Node}");
                    continue;
                }

                await _staging.Ready(hash);
                await VerifiySignatures(self);
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
                _logger.LogCritical($"<<< MemoryPool.ReadySession >>>: Serf client is not ready");
                return;
            }

            await _serfClient.Connect(tcpSession.SessionId);
            var countResult = await _serfClient.MembersCount(tcpSession.SessionId);

            if (!countResult.Success)
            {
                _logger.LogWarning($"<<< MemoryPool.ReadySession >>>: {((SerfError)countResult.NonSuccessMessage).Error}");
            }

            _totalNodes = countResult.Value;
            if (_totalNodes < 4)
            {
                _logger.LogWarning($"<<< MemoryPool.ReadySession >>>: Minimum number of nodes required (4). Total number of nodes ({_totalNodes})");
            }

            if (_totalNodes == 0)
            {
                _logger.LogWarning($"<<< MemoryPool.ReadySession >>>: Total number of nodes ({_totalNodes})");

                _totalNodes = 4;

                _logger.LogWarning($"<<< MemoryPool.ReadySession >>>: Setting default number of nodes ({_totalNodes})");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<bool> VerifiySignatures(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                var staging = await _unitOfWork.StagingRepository.FirstOrDefaultAsync(x => new(x.Hash.Equals(memPool.Block.Hash) && x.Status == StagingState.Blockmainia));
                if (staging != null)
                {
                    var verified = await _validator.VerifyMemPoolSignatures(memPool);
                    if (verified == false)
                    {
                        return false;
                    }

                    Graph.Add(staging.MemPoolProto.ToMemPool());

                    staging.Status = StagingState.Running;

                    await _unitOfWork.StagingRepository.PutAsync(staging, staging.ToIdentifier());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPool.SendToProcess >>>: {ex}");
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private async Task<ulong> LastInterpreted(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(48);

            InterpretedProto blockID = null;

            try
            {
                blockID = await _unitOfWork.InterpretedRepository.LastOrDefaultAsync(x => new(x.Hash == hash.ByteToHex()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"<<< MemoryPool.LastInterpreted >>>: {ex}");
            }
            finally
            {
                _lastInterpretedMessage = blockID switch
                {
                    null => new LastInterpretedMessage(0, null),
                    _ => new LastInterpretedMessage(blockID.Round, blockID),
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
                    var memPool = await _unitOfWork.MemPoolRepository.FirstOrDefaultAsync(x => new(x.Block.Hash.Equals(next.Hash) && x.Block.Node == next.Node && x.Block.Round == next.Round));
                    if (memPool == null)
                    {
                        _logger.LogError($"<<< MemoryPool.BlockmaniaCallback >>>: Unable to find matching block - Hash: {next.Hash} Round: {next.Round} from node {next.Node}");
                        continue;
                    }

                    var verified = await _validator.VerifyMemPoolSignatures(memPool);
                    if (verified == false)
                    {
                        _logger.LogError($"<<< MemoryPool.BlockmaniaCallback >>>: Unable to verify node signatures - Hash: {next.Hash} Round: {next.Round} from node {next.Node}");
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

                    var hasSeen = await _unitOfWork.InterpretedRepository.FirstOrDefaultAsync(x => new(x.Hash.Equals(interpreted.Hash)));
                    if (hasSeen != null)
                    {
                        _logger.LogError($"<<< MemoryPool.BlockmaniaCallback >>>: Already seen interpreted block - Hash: {next.Hash} Round: {next.Round} from node {next.Node}");
                        continue;
                    }

                    var saved = await _unitOfWork.InterpretedRepository.PutAsync(interpreted, interpreted.ToIdentifier());
                    if (saved == null)
                    {
                        _logger.LogError($"<<< MemoryPool.BlockmaniaCallback >>>: Unable to save block for {interpreted.Node} and round {interpreted.Round}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPool.BlockmaniaCallback >>>: {ex}");
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
                ulong round = 0, node = 0;
                bool copy = false;

                copy |= memPool.Block.Node != _serfClient.ClientId;

                if (!copy)
                {
                    node = memPool.Block.Node;
                    round = memPool.Block.Round;
                }

                if (copy)
                {
                    try
                    {
                        round = await IncrementRound(memPool.Block.Hash.HexToByte());
                        node = _serfClient.ClientId;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"<<< MemoryPool.UpsertSelf ->  IncrementRound >>>: {ex}");
                    }
                }

                var prev = await _unitOfWork.MemPoolRepository.PreviousOrDefaultAsync(memPool.Block.Hash.HexToByte(), node, round);
                if (prev != null)
                {
                    memPool.Block.PreviousHash = prev.Block.Hash;

                    if (prev.Block.Round + 1 != memPool.Block.Round)
                        memPool.Prev = prev.Block;
                }

                var signed = await Sign(node, round, memPool);
                stored = await _unitOfWork.MemPoolRepository.PutAsync(signed, signed.ToIdentifier());
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPool.UpsertSelf >>>: {ex}");
            }

            return stored;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="round"></param>
        /// <param name="blockGraph"></param>
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
        /// <returns></returns>
        private async Task<ulong> IncrementRound(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(48);

            ulong round = 0;

            try
            {
                var blockIDs = await _unitOfWork.MemPoolRepository.WhereAsync(x => new ValueTask<bool>(x.Block.Hash.Equals(hash.ByteToHex())));
                if (blockIDs.Any())
                {
                    round = blockIDs.Last().Block.Round;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPool.IncrementRound >>>: {ex}");
            }

            round += 1;

            return round;
        }
    }
}