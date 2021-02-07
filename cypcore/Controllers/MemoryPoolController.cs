// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using CYPCore.Services;
using CYPCore.Models;

namespace CYPCore.Controllers
{
    [Route("pool")]
    [ApiController]
    public class MemoryPoolController
    {
        private readonly IMemoryPoolService _memoryPoolService;
        private readonly ILogger _logger;

        public MemoryPoolController(IMemoryPoolService memoryPoolService, ILogger<MemoryPoolController> logger)
        {
            _memoryPoolService = memoryPoolService;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pool"></param>
        /// <returns></returns>
        [HttpPost(Name = "AddMemoryPool")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddMemoryPool([FromBody] byte[] pool)
        {
            var added = await _memoryPoolService.AddMemoryPool(pool);
            return new ObjectResult(new { code = added == true ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pool"></param>
        /// <returns></returns>
        [HttpPost("pools", Name = "AddMemoryPools")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddMemoryPools([FromBody] byte[] pool)
        {
            await _memoryPoolService.AddMemoryPools(pool);
            return new OkResult();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        [HttpPost("transaction", Name = "AddTransaction")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddTransaction([FromBody] byte[] tx)
        {
            try
            {
                var txProto = Helper.Util.DeserializeProto<TransactionProto>(tx);
                var txByteArray = await _memoryPoolService.AddTransaction(txProto);

                return new ObjectResult(new { protobuf = txByteArray });
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< AddTransaction - Controller >>> {ex}");
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
                _logger.LogError($"<<< MemoryPoolController.GetTransactionCount >>>: {ex}");
            }

            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
