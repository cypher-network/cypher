// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Serilog;

using CYPCore.Ledger;
using CYPCore.Persistence;


namespace CYPNode.Services
{
    public class MempoolService : IMempoolService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMempool _mempool;
        private readonly ILogger _logger;

        public MempoolService(IUnitOfWork unitOfWork, IMempool mempool, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _mempool = mempool;
            _logger = logger.ForContext<MempoolService>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetMempoolTransactionCount()
        {
            var log = _logger.ForContext("Method", "GetMempoolTransactionCount");
            
            int count = 0;

            try
            {
                count = await _unitOfWork.MemPoolRepository.CountAsync();
            }
            catch (Exception ex)
            {
                log.Error("Cannot get mempool transaction count {@Error}", ex);
            }

            return count;
        }
    }
}
