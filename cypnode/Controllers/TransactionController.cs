// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

using Serilog;

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

        public TransactionController(ITransactionService transactionService, ILogger logger)
        {
            _transactionService = transactionService;
            _logger = logger.ForContext<TransactionController>();
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
            var log = _logger.ForContext("Method", "AddTransaction");

            try
            {
                var txProto = CYPCore.Helper.Util.DeserializeProto<TransactionProto>(tx);
                var txByteArray = await _transactionService.AddTransaction(txProto);

                return new ObjectResult(new { protobuf = txByteArray });
            }
            catch (Exception ex)
            {
                log.Error("Cannot add transaction {@Error}", ex);
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
            var log = _logger.ForContext("Method", "GetTransaction");
            
            try
            {
                var tx = await _transactionService.GetTransaction(txnid.HexToByte());
                return new ObjectResult(new { protobufs = tx });
            }
            catch (Exception ex)
            {
                log.Error("Cannot get transaction {@Error}", ex);
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("safeguard", Name = "GetSafeguardTransactions")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSafeguardTransactions()
        {
            var log = _logger.ForContext("Method", "GetSafeguardTransactions");
            
            try
            {
                var safeGuardTransactions = await _transactionService.GetSafeguardTransactions();
                return new ObjectResult(new { protobufs = safeGuardTransactions });
            }
            catch (Exception ex)
            {
                log.Error("Cannot get safeguard transactions {@Error}", ex);
            }

            return NotFound();
        }
    }
}
