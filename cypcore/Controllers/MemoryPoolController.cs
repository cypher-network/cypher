// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using CYPCore.Extensions;
using CYPCore.Ledger;
using CYPCore.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using FlatSharp;

namespace CYPCore.Controllers
{
    [Route("mem")]
    [ApiController]
    public class MemoryPoolController
    {
        private readonly IMemoryPool _memoryPool;
        private readonly ILogger _logger;

        public MemoryPoolController(IMemoryPool memoryPool, ILogger logger)
        {
            _memoryPool = memoryPool;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPoolController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        [HttpPost("transaction", Name = "AddTransaction")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult AddTransaction([FromBody] byte[] tx)
        {
            try
            {
                var payload = FlatBufferSerializer.Default.Parse<TransactionProto>(tx);
                var added = _memoryPool.Add(payload);

                return added switch
                {
                    VerifyResult.Succeed => new ObjectResult(StatusCodes.Status200OK),
                    VerifyResult.AlreadyExists => new ConflictObjectResult(StatusCodes.Status409Conflict),
                    _ => new BadRequestObjectResult(StatusCodes.Status500InternalServerError),
                };
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to add the memory pool transaction");
            }

            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("count", Name = "GetCount")]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetCount()
        {
            try
            {
                var count = _memoryPool.Count();
                return new ObjectResult(new { count });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the memory pool transaction count");
            }

            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
