// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using CYPCore.Services;
using CYPCore.Extentions;

namespace CYPCore.Controllers
{
    [Route("header")]
    [ApiController]
    public class BlockController : Controller
    {
        private readonly IBlockService _blockService;
        private readonly ILogger _logger;

        public BlockController(IBlockService blockService, ILogger<BlockController> logger)
        {
            _blockService = blockService;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        [HttpPost("block", Name = "AddBlock")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddBlock([FromBody] byte[] payload)
        {
            var added = await _blockService.AddBlock(payload);
            return new ObjectResult(new { added });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payloads"></param>
        /// <returns></returns>
        [HttpPost("blocks", Name = "AddBlocks")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddBlocks([FromBody] byte[] payloads)
        {
            await _blockService.AddBlocks(payloads);
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
                _logger.LogError($"<<< BlockController.GetSafeguardBlocks >>> {ex}");
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
                _logger.LogError($"<<< BlockController.GetBlockHeight >>> {ex}");
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
                _logger.LogError($"<<< BlockController.GetRange >>> {ex}");
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
                _logger.LogError($"<<< BlockController.GetVout >>> {ex}");
            }

            return NotFound();
        }
    }
}
