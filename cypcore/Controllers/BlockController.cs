// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;
using CYPCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Serilog;

using CYPCore.Services;
using CYPCore.Extentions;
using CYPCore.Models;
using Org.BouncyCastle.Crypto.Tls;

namespace CYPCore.Controllers
{
    [Route("header")]
    [ApiController]
    public class BlockController : Controller
    {
        private readonly IBlockService _blockService;
        private readonly ILogger _logger;

        public BlockController(IBlockService blockService, ILogger logger)
        {
            _blockService = blockService;
            _logger = logger.ForContext("SourceContext", nameof(BlockController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        [HttpPost("block", Name = "AddBlock")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddBlock([FromBody] PayloadProto block)
        {
            var added = await _blockService.AddBlock(block);
            return new ObjectResult(new { code = added == true ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blocks"></param>
        /// <returns></returns>
        [HttpPost("blocks", Name = "AddBlocks")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddBlocks([FromBody] PayloadProto[] blocks)
        {
            await _blockService.AddBlocks(blocks);
            return new OkResult();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("safeguardblocks", Name = "GetSafeguardBlocks")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSafeguardBlocks()
        {
            try
            {
                var safeGuardTransactions = await _blockService.GetSafeguardBlocks();
                return new ObjectResult(new { protobufs = Helper.Util.SerializeProto(safeGuardTransactions) });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get safeguard blocks");
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("height", Name = "GetBlockHeight")]
        [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBlockHeight()
        {
            try
            {
                var height = await _blockService.GetHeight();
                return new ObjectResult(new { height });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get block height");
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        [HttpGet("blocks/{skip}/{take}", Name = "GetRange")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetRange(int skip, int take)
        {
            try
            {
                var blocks = await _blockService.GetBlockHeaders(skip, take);
                return new ObjectResult(new { protobufs = Helper.Util.SerializeProto(blocks) });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get range");
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="txnid"></param>
        /// <returns></returns>
        [HttpGet("vout/{txnid}", Name = "GetVout")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetVout(string txnid)
        {
            try
            {
                var tx = await _blockService.GetVout(txnid.HexToByte());
                return new ObjectResult(new { protobufs = tx });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get Vout");
            }

            return NotFound();
        }
    }
}
