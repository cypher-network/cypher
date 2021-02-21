// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Serilog;

using CYPCore.Services;

namespace CYPCore.Controllers
{
    [Route("pool")]
    [ApiController]
    public class MemoryPoolController
    {
        private readonly IMemoryPoolService _memoryPoolService;
        private readonly ILogger _logger;

        public MemoryPoolController(IMemoryPoolService memoryPoolService, ILogger logger)
        {
            _memoryPoolService = memoryPoolService;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPoolController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pool"></param>
        /// <returns></returns>
        [HttpPost(Name = "AddMemoryPool")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddMemoryPool([FromBody] byte[] pool)
        {
            var payload = Helper.Util.DeserializeProto<MemPoolProto>(pool);
            var added = await _memoryPoolService.AddMemoryPool(payload);

            return new ObjectResult(new { code = added ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pools"></param>
        /// <returns></returns>
        [HttpPost("pools", Name = "AddMemoryPools")]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddMemoryPools([FromBody] byte[] pools)
        {
            var payloads = Helper.Util.DeserializeListProto<MemPoolProto>(pools).ToArray();
            await _memoryPoolService.AddMemoryPools(payloads);

            return new OkResult();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        [HttpPost("transaction", Name = "AddTransaction")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddTransaction([FromBody] byte[] tx)
        {
            try
            {
                var payload = Helper.Util.DeserializeProto<TransactionProto>(tx);
                var added = await _memoryPoolService.AddTransaction(payload);

                return new ObjectResult(new { code = added ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot add transaction");
            }

            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("count", Name = "GetTransactionCount")]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTransactionCount()
        {
            try
            {
                var transactionCount = await _memoryPoolService.GetTransactionCount();
                return new ObjectResult(new { count = transactionCount });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get transaction count");
            }

            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
