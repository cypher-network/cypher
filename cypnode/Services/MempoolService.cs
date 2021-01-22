// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using CYPCore.Ledger;
using CYPCore.Persistence;


namespace CYPNode.Services
{
    public class MempoolService : IMempoolService
    {
        private readonly IUnitOfWork _unitOfWork;
        // TODO: Check deletion. _mempool is currently unused.
        private readonly IMempool _mempool;
        private readonly ILogger _logger;

        /// <summary>
        ///
        /// </summary>
        /// <param name="unitOfWork"></param>
        /// <param name="mempool"></param>
        /// <param name="logger"></param>
        public MempoolService(IUnitOfWork unitOfWork, IMempool mempool, ILogger<MempoolService> logger)
        {
            _unitOfWork = unitOfWork;
            _mempool = mempool;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetMempoolTransactionCount()
        {
            int count = 0;

            try
            {
                count = await _unitOfWork.MemPoolRepository.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MempoolService.GetMempoolBlockHeight >>>: {ex}");
            }

            return count;
        }
    }
}
