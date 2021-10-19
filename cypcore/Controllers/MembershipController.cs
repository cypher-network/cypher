// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Network;
using CYPCore.Network.Commands;
using CYPCore.Network.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Proto;
using Proto.DependencyInjection;

namespace CYPCore.Controllers
{
    [Route("member")]
    [ApiController]
    public class MembershipController : Controller
    {
        private readonly ActorSystem _actorSystem;
        private readonly PID _pidShimCommand;
        private readonly PID _pidLocalNode;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        public MembershipController(ActorSystem actorSystem, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidShimCommand = _actorSystem.Root.Spawn(_actorSystem.DI().PropsFor<ShimCommands>());
            _pidLocalNode = _actorSystem.Root.Spawn(_actorSystem.DI().PropsFor<LocalNode>());
            _logger = logger.ForContext("SourceContext", nameof(MembershipController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("/peers", Name = "GetMembers")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMembers()
        {
            try
            {
                var peersMemStoreResponse =
                    await _actorSystem.Root.RequestAsync<PeersMemStoreResponse>(_pidLocalNode,
                        new PeersMemStoreRequest());
                await _actorSystem.Root.StopAsync(_pidShimCommand);
                var snapshot = await peersMemStoreResponse.MemStore.GetMemSnapshot().SnapshotAsync().ToArrayAsync();
                return new ObjectResult(new { peers = snapshot.Select(x => x.Value) });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("/peer", Name = "GetMember")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMember()
        {
            try
            {
                var response = await _actorSystem.Root.RequestAsync<PeerResponse>(_pidShimCommand, new PeerRequest());
                await _actorSystem.Root.StopAsync(_pidShimCommand);
                return new ObjectResult(new { peer = response.Peer });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("/peers/count", Name = "GetMemberCount")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMemberCount()
        {
            try
            {
                var peersMemStoreResponse =
                    await _actorSystem.Root.RequestAsync<PeersMemStoreResponse>(_pidLocalNode,
                        new PeersMemStoreRequest());
                await _actorSystem.Root.StopAsync(_pidShimCommand);
                return new ObjectResult(new { count = peersMemStoreResponse.MemStore.Count() });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return NotFound();
        }
    }
}
