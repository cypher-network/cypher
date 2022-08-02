// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace CypherNetwork.Controllers;

[Route("member")]
[ApiController]
public class MembershipController : Controller
{
    private readonly ICypherNetworkCore _cypherNetworkCore;
    private readonly ILogger _logger;

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public MembershipController(ICypherNetworkCore cypherNetworkCore, ILogger logger)
    {
        _cypherNetworkCore = cypherNetworkCore;
        _logger = logger.ForContext("SourceContext", nameof(MembershipController));
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [HttpGet("peer", Name = "GetPeer")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPeerAsync()
    {
        try
        {
            var peer =  (await _cypherNetworkCore.PeerDiscovery()).GetLocalPeer();
            return new ObjectResult(new
            {
                 Advertise = peer.Advertise.FromBytes(),
                 BlockHeight = peer.BlockCount,
                 peer.ClientId,
                 HttpEndPoint = peer.HttpEndPoint.FromBytes(),
                 Listening = peer.Listening.FromBytes(),
                 Name = peer.Name.FromBytes(),
                 PublicKey = peer.PublicKey.ByteToHex(),
                 Version = peer.Version.FromBytes()
            });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex.Message);
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [HttpGet("peers", Name = "GetPeers")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPeersAsync()
    {
        try
        {
            var peers = await (await _cypherNetworkCore.PeerDiscovery()).GetDiscoveryAsync();
            return new ObjectResult(peers.Select(x => new
            {
                Advertise = x.Advertise.FromBytes(),
                BlockHeight = x.BlockCount,
                x.ClientId,
                HttpEndPoint = x.HttpEndPoint.FromBytes(),
                Listening = x.Listening.FromBytes(),
                Name = x.Name.FromBytes(),
                PublicKey = x.PublicKey.ByteToHex(),
                Version = x.Version.FromBytes()
            }));
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex.Message);
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [HttpGet("count", Name = "GetPeersCount")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPeersCountAsync()
    {
        try
        {
            return new ObjectResult(new { count = (await _cypherNetworkCore.PeerDiscovery()).Count() });
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return NotFound();
    }
}