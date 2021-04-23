// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using CYPCore.Consensus.Models;
using CYPCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using CYPCore.Extentions;
using CYPCore.Ledger;
using CYPCore.Models;
using FlatSharp;

namespace CYPCore.Controllers
{
    [Route("chain")]
    [ApiController]
    public class BlockController : Controller
    {
        private readonly IGraph _graph;
        private readonly ILogger _logger;

        public BlockController(IGraph graph, ILogger logger)
        {
            _graph = graph;
            _logger = logger.ForContext("SourceContext", nameof(BlockController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraphModel"></param>
        /// <returns></returns>
        [HttpPost("blockgraph", Name = "AddBlockGraph")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddBlockGraph([FromBody] byte[] blockGraphModel)
        {
            try
            {
                var added = await _graph.TryAddBlockGraph(blockGraphModel);
                return new ObjectResult(new { code = added == VerifyResult.Succeed ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to add the block graph");
            }

            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
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
                var safeGuardTransactions = await _graph.GetSafeguardBlocks();
                var blockHeaders = safeGuardTransactions as BlockHeaderProto[] ?? safeGuardTransactions.ToArray();
                var genericList = new GenericList<BlockHeaderProto> { Data = blockHeaders };
                var maxBytesNeeded = FlatBufferSerializer.Default.GetMaxSize(genericList);
                var buffer = new byte[maxBytesNeeded];

                FlatBufferSerializer.Default.Serialize(genericList, buffer);

                return new ObjectResult(new { flatbuffers = buffer });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the safeguard blocks");
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
                var height = await _graph.GetHeight();
                return new ObjectResult(new { height });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the block height");
            }

            return NotFound();
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        [HttpGet("hash/{height}", Name = "GetHash")]
        [ProducesResponseType(typeof(BlockHash), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetHash(long height)
        {
            try
            {
                var hash = await _graph.GetHash(height);
                return new ObjectResult(new { hash });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the block hash for height {@Height}", height);
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        [HttpGet("blocks/{skip}/{take}", Name = "GetBlocks")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBlocks(int skip, int take)
        {
            try
            {
                var blocks = await _graph.GetBlocks(skip, take);
                var genericList = new GenericList<BlockHeaderProto> { Data = blocks.ToList() };
                var maxBytesNeeded = FlatBufferSerializer.Default.GetMaxSize(genericList);
                var buffer = new byte[maxBytesNeeded];

                FlatBufferSerializer.Default.Serialize(genericList, buffer);

                return new ObjectResult(new { flatbuffer = buffer });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the range");
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("transaction/{id}", Name = "GetTransaction")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTransaction(string id)
        {
            try
            {
                var transaction = await _graph.GetTransaction(id.HexToByte());
                if (transaction != null)
                {
                    var genericList = new GenericList<VoutProto> { Data = transaction.ToList() };
                    var maxBytesNeeded = FlatBufferSerializer.Default.GetMaxSize(genericList);
                    var buffer = new byte[maxBytesNeeded];

                    FlatBufferSerializer.Default.Serialize(genericList, buffer);

                    return new ObjectResult(new { flatbuffers = buffer });
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the transaction");
            }

            return NotFound();
        }
    }
}
