// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Threading.Tasks;
using CYPCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using CYPCore.Network.Commands;
using CYPCore.Network.Messages;
using Dawn;
using MessagePack;
using Proto;
using Proto.DependencyInjection;

namespace CYPCore.Controllers
{
    [Route("chain")]
    [ApiController]
    public class BlockController : Controller
    {
        private readonly ActorSystem _actorSystem;
        private readonly PID _pid;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        public BlockController(ActorSystem actorSystem, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pid= _actorSystem.Root.Spawn(_actorSystem.DI().PropsFor<ShimCommands>());
            _logger = logger.ForContext("SourceContext", nameof(BlockController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("/safeguards", Name = "GetSafeguardBlocks")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSafeguardBlocks()
        {
            try
            {
                var response =
                    await _actorSystem.Root.RequestAsync<SafeguardBlocksResponse>(_pid,
                        new SafeguardBlocksRequest(147));
                await _actorSystem.Root.StopAsync(_pid);
                await using var stream = new MemoryStream();
                MessagePackSerializer.SerializeAsync(stream, response.Blocks).Wait();
                return new ObjectResult(new { messagepack = stream.ToArray() });
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
        [HttpGet("/height", Name = "GetBlockHeight")]
        [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBlockHeight()
        {
            try
            {
                var response = await _actorSystem.Root.RequestAsync<BlockCountResponse>(_pid, new BlockCountRequest());
                await _actorSystem.Root.StopAsync(_pid);
                return new ObjectResult(new { height = response.Count });
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
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        [HttpGet("/blocks/{skip}/{take}", Name = "GetBlocks")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBlocks(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();
            try
            {
                var response =
                    await _actorSystem.Root.RequestAsync<BlocksResponse>(_pid, new BlocksRequest(skip, take));
                await _actorSystem.Root.StopAsync(_pid);
                return new ObjectResult(new { messagepack = response.Blocks });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get blocks");
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("/transaction/{id}", Name = "GetTransaction")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTransaction(string id)
        {
            Guard.Argument(id, nameof(id)).NotNull().NotEmpty().NotWhiteSpace();
            try
            {
                var response =
                    await _actorSystem.Root.RequestAsync<TransactionResponse>(_pid,
                        new TransactionRequest(id.HexToByte()));
                await _actorSystem.Root.StopAsync(_pid);
                return new ObjectResult(new { messagepack = response.Transaction });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the transaction");
            }

            return NotFound();
        }
    }
}
