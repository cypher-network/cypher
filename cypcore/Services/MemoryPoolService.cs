// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Ledger;
using CYPCore.Persistence;
using CYPCore.Models;
using CYPCore.Extentions;
using CYPCore.Serf;
using CYPCore.Cryptography;

namespace CYPCore.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class MemoryPoolService : IMemoryPoolService
    {
        private readonly IUnitOfWork _unitOfWork;
        // TODO: Check deletion. _mempool is currently unused.
        private readonly IMemoryPool _mempool;
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signingProvider;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="unitOfWork"></param>
        /// <param name="mempool"></param>
        /// <param name="serfClient"></param>
        /// <param name="signingProvider"></param>
        /// <param name="logger"></param>
        public MemoryPoolService(IUnitOfWork unitOfWork, IMemoryPool mempool,
            ISerfClient serfClient, ISigning signingProvider, ILogger<MemoryPoolService> logger)
        {
            _unitOfWork = unitOfWork;
            _mempool = mempool;
            _serfClient = serfClient;
            _signingProvider = signingProvider;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task<byte[]> AddTransaction(TransactionProto tx)
        {
            Guard.Argument(tx, nameof(tx)).NotNull();

            try
            {
                bool valid = tx.Validate().Any();
                if (!valid)
                {
                    bool delivered = await DeliveredTxExist(tx);
                    if (delivered)
                    {
                        return await Payload(tx, "K_Image exists.", true);
                    }

                    bool mempool = await MempoolTxExist(tx);
                    if (mempool)
                    {
                        return await Payload(tx, "Exists in mempool.", true);
                    }

                    var memPoolProto = await _mempool.AddTransaction(MempoolProtoFactory(tx));
                    if (memPoolProto == null)
                    {
                        return await Payload(tx, "Unable to add txn mempool.", true);
                    }

                    return await Payload(memPoolProto.Block.Transaction, string.Empty, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< TransactionService.AddTransaction >>>: {ex}");
            }

            return await Payload(tx, "No message", true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetTransactionCount()
        {
            int count = 0;

            try
            {
                count = await _unitOfWork.MemPoolRepository.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPoolService.GetMempoolBlockHeight >>>: {ex}");
            }

            return count;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pools"></param>
        /// <returns></returns>
        public async Task AddMemoryPools(byte[] pools)
        {
            try
            {
                var memPools = Helper.Util.DeserializeListProto<MemPoolProto>(pools);
                if (memPools.Any())
                {
                    foreach (var mempool in memPools)
                    {
                        var processed = await Process(mempool);
                        if (!processed)
                        {

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPoolService.AddMemoryPools >>>: {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pool"></param>
        /// <returns></returns>
        public async Task<bool> AddMemoryPool(byte[] pool)
        {
            bool processed = false;

            try
            {
                var payload = Helper.Util.DeserializeProto<MemPoolProto>(pool);
                if (payload != null)
                {
                    processed = await Process(payload);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPoolService.AddMemoryPool >>>: {ex}");
            }

            return processed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private async Task<bool> Process(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            if (_serfClient.ClientId == memPool.Block.Node)
            {
                return false;
            }

            memPool.Included = false;
            memPool.Replied = false;

            var added = await _mempool.AddTransaction(memPool);
            if (added == null)
            {
                _logger.LogError($"<<< MemoryPoolService.Process >>>: " +
                    $"Blockgraph: {memPool.Block.Hash} was not add " +
                    $"for node {memPool.Block.Node} and round {memPool.Block.Round}");

                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<bool> MempoolTxExist(TransactionProto tx)
        {
            var memPool = await _unitOfWork.MemPoolRepository.FirstOrDefaultAsync(x => new(
                x.Block.Hash.Equals(tx.ToHash().ByteToHex()) &&
                x.Block.Transaction.Ver == tx.Ver &&
                x.Block.Node == _serfClient.ClientId));

            return memPool != null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<bool> DeliveredTxExist(TransactionProto tx)
        {
            foreach (var vin in tx.Vin)
            {
                var exists = await _unitOfWork.DeliveredRepository
                    .FirstOrDefaultAsync(x => new(x.Transactions.Any(t => t.Vin.First().Key.K_Image.Xor(vin.Key.K_Image))));

                if (exists != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="message"></param>
        /// <param name="isError"></param>
        /// <returns></returns>
        private async Task<byte[]> Payload(TransactionProto tx, string message, bool isError)
        {
            byte[] data = CYPCore.Helper.Util.SerializeProto(tx);
            var payload = new PayloadProto
            {
                Error = isError,
                Message = message,
                Node = _serfClient.ClientId,
                Data = data,
                PublicKey = await _signingProvider.GetPublicKey(_signingProvider.DefaultSigningKeyName),
                Signature = await _signingProvider.Sign(_signingProvider.DefaultSigningKeyName, Helper.Util.SHA384ManagedHash(data))
            };

            return Helper.Util.SerializeProto(payload);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private MemPoolProto MempoolProtoFactory(TransactionProto tx)
        {
            return new()
            {
                Block = new InterpretedProto
                {
                    Hash = tx.ToKeyImage().ByteToHex(),
                    Node = _serfClient.ClientId,
                    Round = 0,
                    Transaction = tx
                },
                Deps = new List<DepProto>()
            };
        }
    }
}