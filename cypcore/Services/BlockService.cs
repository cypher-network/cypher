// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Dawn;
using Serilog;

using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Extentions;
using CYPCore.Persistence;
using CYPCore.Models;
using CYPCore.Ledger;

namespace CYPCore.Services
{
    public class BlockService : IBlockService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _logger;
        private readonly ISigning _signingProvider;
        private readonly IValidator _validator;

        public BlockService() { }

        public BlockService(IUnitOfWork unitOfWork, ISigning signingProvider, IValidator validator, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _signingProvider = signingProvider;
            _validator = validator;
            _logger = logger.ForContext("SourceContext", nameof(BlockService));
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
                _logger.Here().Error(ex, "Cannot get block headers");
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
                var last = await _unitOfWork.DeliveredRepository.LastAsync();

                if (last != null)
                {
                    var height = last.Height - count;

                    height = height > 0 ? 0 : height;

                    blockHeaders = await _unitOfWork.DeliveredRepository.RangeAsync(height, 147);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get safeguard blocks");
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
                var last = await _unitOfWork.DeliveredRepository.LastAsync();
                if (last != null)
                {
                    height = last.Height + 1;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get block height");
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
            Guard.Argument(txnId, nameof(txnId)).NotNull().MaxCount(32);

            byte[] transaction = null;

            try
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x => new ValueTask<bool>(x.Transactions.Any(t => t.TxnId.Xor(txnId))));
                var firstBlockHeader = blockHeaders.FirstOrDefault();
                var found = firstBlockHeader?.Transactions.FirstOrDefault(x => x.TxnId.Xor(txnId));
                if (found != null)
                {
                    transaction = Helper.Util.SerializeProto(found.Vout);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get Vout");
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
                        _logger.Here().Error("Unable to process the block header");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot add block");
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
                var payloadProtos = Helper.Util.DeserializeListProto<PayloadProto>(payloads).ToList();
                if (payloadProtos.Any())
                {
                    foreach (var payload in payloadProtos)
                    {
                        var processed = await Process(payload);
                        if (!processed)
                        {
                            _logger.Here().Error("Unable to process the block header");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot add block");
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
                _logger.Here().Error("Unable to verify signature");
                return false;
            }

            var blockHeader = Helper.Util.DeserializeProto<BlockHeaderProto>(payload.Data);

            await _validator.GetRunningDistribution();

            verified = await _validator.VerifyBlockHeader(blockHeader);
            if (!verified)
            {
                _logger.Here().Error("Unable to verify block header");
            }

            var saved = await _unitOfWork.DeliveredRepository.PutAsync(blockHeader.ToIdentifier(), blockHeader);
            if (saved) return true;

            _logger.Here().Error("Unable to save block header: {@MerkleRoot}", blockHeader.MrklRoot);

            return false;
        }
    }
}

