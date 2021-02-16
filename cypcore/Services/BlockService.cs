// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Cryptography;
using CYPCore.Persistence;
using CYPCore.Models;
using CYPCore.Ledger;
using CYPCore.Extentions;

namespace CYPCore.Services
{
    public class BlockService : IBlockService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _logger;
        private readonly ISigning _signingProvider;
        private readonly IValidator _validator;

        public BlockService() { }

        public BlockService(IUnitOfWork unitOfWork, ISigning signingProvider, IValidator validator, ILogger<BlockService> logger)
        {
            _unitOfWork = unitOfWork;
            _signingProvider = signingProvider;
            _validator = validator;
            _logger = logger;
        }  

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<IEnumerable<BlockHeaderProto>> GetBlockHeaders(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            var blockHeaders = Enumerable.Empty<BlockHeaderProto>();

            try
            {
                blockHeaders = await _unitOfWork.DeliveredRepository.RangeAsync(skip, take);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockService.GetBlocks >>>: {ex}");
            }           

            return blockHeaders;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<BlockHeaderProto>> GetSafeguardBlocks()
        {
            var blockHeaders = Enumerable.Empty<BlockHeaderProto>();

            try
            {
                var count = await _unitOfWork.DeliveredRepository.CountAsync();
                var last = await _unitOfWork.DeliveredRepository.LastOrDefaultAsync();

                if (last != null)
                {
                    int height = (int)last.Height - count;

                    height = height > 0 ? 0 : height;

                    blockHeaders = await _unitOfWork.DeliveredRepository.RangeAsync(height, 147);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockService.GetSafeguardBlocks >>>: {ex}");
            }

            return blockHeaders;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<long> GetHeight()
        {
            long height = 0;

            try
            {
                var last = await _unitOfWork.DeliveredRepository.LastOrDefaultAsync();
                if (last != null)
                {
                    height = last.Height + 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockService.GetBlockHeight >>>: {ex}");
            }

            return height;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="txnId"></param>
        /// <returns></returns>
        public async Task<byte[]> GetVout(byte[] txnId)
        {
            Guard.Argument(txnId, nameof(txnId)).NotNull().MaxCount(48);

            byte[] transaction = null;

            try
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x => x.Transactions.Any(t => t.TxnId.Xor(txnId)));
                var firstBlockHeader = blockHeaders.FirstOrDefault();
                var found = firstBlockHeader?.Transactions.FirstOrDefault(x => x.TxnId.Xor(txnId));
                if (found != null)
                {
                    transaction = Helper.Util.SerializeProto(found.Vout);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockService.GetVout >>>: {ex}");
            }

            return transaction;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<bool> AddBlock(byte[] payload)
        {
            bool processed = false;

            try
            {
                var payloadProto = Helper.Util.DeserializeProto<PayloadProto>(payload);
                if (payloadProto != null)
                {
                    processed = await Process(payloadProto);
                    if (!processed)
                    {
                        _logger.LogError($"<<< BlockService.AddBlock >>>: Unable to process the block header");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockService.AddBlock >>> {ex}");
            }

            return processed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payloads"></param>
        /// <returns></returns>
        public async Task AddBlocks(byte[] payloads)
        {
            try
            {
                var payloadProtos = Helper.Util.DeserializeListProto<PayloadProto>(payloads);
                if (payloadProtos.Any())
                {
                    foreach (var payload in payloadProtos)
                    {
                        var processed = await Process(payload);
                        if (!processed)
                        {
                            _logger.LogError($"<<< BlockService.AddBlocks >>>: Unable to process the block header");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockService.AddBlocks >>> {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        private async Task<bool> Process(PayloadProto payload)
        {
            Guard.Argument(payload, nameof(payload)).NotNull();

            var verified = _signingProvider.VerifySignature(payload.Signature, payload.PublicKey, Helper.Util.SHA384ManagedHash(payload.Data));
            if (!verified)
            {
                _logger.LogError($"<<< BlockService.Process >>: Unable to verifiy signature.");
                return false;
            }

            var blockHeader = Helper.Util.DeserializeProto<BlockHeaderProto>(payload.Data);

            await _validator.GetRunningDistribution();

            verified = await _validator.VerifyBlockHeader(blockHeader);
            if (!verified)
            {
                _logger.LogError($"<<< BlockService.Process >>: Unable to verifiy block header.");
            }

            int? saved = await _unitOfWork.DeliveredRepository.SaveOrUpdateAsync(blockHeader);
            if (!saved.HasValue)
            {
                _logger.LogError($"<<< BlockService.Process >>>: Unable to save block header: {blockHeader.MrklRoot}");
                return false;
            }

            return true;
        }
    }
}

