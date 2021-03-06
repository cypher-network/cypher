// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
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
        /// <param name="memPool"></param>
        /// <returns></returns>
        [HttpPost(Name = "AddMemoryPool")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddMemoryPool([FromBody] MemPoolProto memPool)
        {
            var added = await _memoryPoolService.AddMemoryPool(memPool);

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
        public async Task<IActionResult> AddMemoryPools([FromBody] MemPoolProto[] pools)
        {
            await _memoryPoolService.AddMemoryPools(pools);

            return new OkResult();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        [HttpPost("transaction", Name = "AddTransaction")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddTransaction([FromBody] TransactionProto transaction)
        {
            try
            {
                var added = await _memoryPoolService.AddTransaction(transaction);

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
