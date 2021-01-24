// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Cryptography;
using CYPCore.Persistence;
using CYPCore.Models;

namespace CYPNode.Services
{
    public class BlockService : IBlockService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _logger;
        private readonly ISigning _signingProvider;

        public BlockService(IUnitOfWork unitOfWork, ISigning signingProvider, ILogger<BlockService> logger)
        {
            _unitOfWork = unitOfWork;
            _signingProvider = signingProvider;
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
                    height = last.Height;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockService.GetBlockHeight >>>: {ex}");
            }

            return height;
        }
    }
}
