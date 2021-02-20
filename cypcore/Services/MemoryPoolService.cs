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

namespace CYPCore.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class MemoryPoolService : IMemoryPoolService
    {
        private readonly IUnitOfWork _unitOfWork;

        // TODO: Check deletion. _memoryPool is currently unused.
        private readonly IMemoryPool _memoryPool;
        private readonly ISerfClient _serfClient;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="unitOfWork"></param>
        /// <param name="memoryPool"></param>
        /// <param name="serfClient"></param>
        /// <param name="logger"></param>
        public MemoryPoolService(IUnitOfWork unitOfWork, IMemoryPool memoryPool,
            ISerfClient serfClient, ILogger<MemoryPoolService> logger)
        {
            _unitOfWork = unitOfWork;
            _memoryPool = memoryPool;
            _serfClient = serfClient;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task<bool> AddTransaction(TransactionProto tx)
        {
            Guard.Argument(tx, nameof(tx)).NotNull();

            try
            {
                var valid = tx.Validate().Any();
                if (!valid)
                {
                    var delivered = await TransactionDeliveredExist(tx);
                    if (delivered)
                    {
                        return false;
                    }

                    var memory = await TransactionMemoryPoolExist(tx);
                    if (memory)
                    {
                        return false;
                    }

                    var memPoolProto = await _memoryPool.AddTransaction(MemoryPoolProtoFactory(tx));
                    if (memPoolProto == null)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< TransactionService.AddTransaction >>>: {ex}");
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<long> GetTransactionCount()
        {
            var count = 0L;

            try
            {
                count = await _unitOfWork.MemPoolRepository.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemoryPoolService.GetTransactionCount >>>: {ex}");
            }

            return count;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pools"></param>
        /// <returns></returns>
        public async Task AddMemoryPools(MemPoolProto[] memPools)
        {
            try
            {
                if (memPools.Any())
                {
                    foreach (var memPool in memPools)
                    {
                        var processed = await Process(memPool);
                        if (!processed)
                        {
                            _logger.LogError(
                                $"<<< MemoryPoolService.AddMemoryPools >>>: Could not process memory pool with hash:  {memPool.Block.Hash}");
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
        /// <param name="memPool"></param>
        /// <returns></returns>
        public async Task<bool> AddMemoryPool(MemPoolProto memPool)
        {
            var processed = false;

            try
            {
                processed = await Process(memPool);
                if (!processed)
                {
                    _logger.LogError(
                        $"<<< MemoryPoolService.AddMemoryPool >>>: Could not process memory pool with hash:  {memPool.Block.Hash}");
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

            if (_serfClient.ClientId == memPool.Block.Node) return false;

            memPool.Included = false;
            memPool.Replied = false;

            var added = await _memoryPool.AddTransaction(memPool);
            if (added != null) return true;
            _logger.LogError($"<<< MemoryPoolService.Process >>>: " +
                             $"Memory pool hash: {memPool.Block.Hash} was not added " +
                             $"for node {memPool.Block.Node} and round {memPool.Block.Round}");
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<bool> TransactionMemoryPoolExist(TransactionProto tx)
        {
            var memPool = await _unitOfWork.MemPoolRepository.FirstAsync(x =>
                new ValueTask<bool>(
                    x.Block.Hash.Equals(tx.ToHash().ByteToHex()) && x.Block.Node == _serfClient.ClientId));

            return memPool != null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<bool> TransactionDeliveredExist(TransactionProto tx)
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private MemPoolProto MemoryPoolProtoFactory(TransactionProto tx)
        {
            return new()
            {
                Block = new InterpretedProto
                {
                    Hash = tx.ToHash().ByteToHex(),
                    Node = _serfClient.ClientId,
                    Round = 1,
                    Transaction = tx
                },
                Deps = new List<DepProto>()
            };
        }
    }
}