// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using CYPNode.Services;

namespace cypnode.Controllers
{
    [Route("api/blocks")]
    public class BlocksController : Controller
    {
        private readonly IBlockService _blockService;
        private readonly ILogger _logger;

        public BlocksController(IBlockService blockService, ILogger<BlocksController> logger)
        {
            _blockService = blockService;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("safeguard", Name = "GetSafeguardBlocks")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSafeguardBlocks()
        {
            try
            {
                var safeGuardTransactions = await _blockService.GetSafeguardBlocks();
                return new ObjectResult(new { protobufs = CYPCore.Helper.Util.SerializeProto(safeGuardTransactions) });
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< GetSafeguardBlocks - Controller >>> {ex}");
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
                _logger.LogError($"<<< GetBlockHeight - Controller >>> {ex}");
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        [HttpGet("range/{skip}/{take}", Name = "GetRange")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetRange(int skip, int take)
        {
            try
            {
                var blocks = await _blockService.GetBlockHeaders(skip, take);
                return new ObjectResult(new { protobufs = CYPCore.Helper.Util.SerializeProto(blocks) });
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< GetRange - Controller >>> {ex}");
            }

            return NotFound();
        }
    }
}
