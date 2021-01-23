// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

using CYPNode.Services;
using CYPCore.Models;
using CYPCore.Extentions;

namespace CYPNode.Controllers
{
    [Route("api/transaction")]
    [ApiController]
    public class TransactionController : Controller
    {
        private readonly ITransactionService _transactionService;
        private readonly ILogger _logger;

        public TransactionController(ITransactionService transactionService, ILogger<TransactionController> logger)
        {
            _transactionService = transactionService;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="coin"></param>
        /// <returns></returns>
        [HttpPost("mempool", Name = "AddTransaction")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddTransaction([FromBody] byte[] tx)
        {
            try
            {
                var txProto = CYPCore.Helper.Util.DeserializeProto<TransactionProto>(tx);
                var txByteArray = await _transactionService.AddTransaction(txProto);

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
        /// <param name="txnId"></param>
        /// <returns></returns>
        [HttpGet("{txnid}", Name = "GetTransaction")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTransaction(string txnid)
        {
            try
            {
                var tx = await _transactionService.GetTransaction(txnid.HexToByte());
                return new ObjectResult(new { protobufs = tx });
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< GetTransaction - Controller >>> {ex}");
            }

            return NotFound();
        }
    }
}
