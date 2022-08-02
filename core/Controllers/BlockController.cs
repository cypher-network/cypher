﻿// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Models.Messages;
using Dawn;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace CypherNetwork.Controllers;

[Route("chain")]
[ApiController]
public class BlockController : Controller
{
    private readonly ICypherNetworkCore _cypherNetworkCore;
    private readonly ILogger _logger;

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public BlockController(ICypherNetworkCore cypherNetworkCore, ILogger logger)
    {
        _cypherNetworkCore = cypherNetworkCore;
        _logger = logger.ForContext("SourceContext", nameof(BlockController));
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [HttpGet("block", Name = "GetBlock")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBlockAsync(string hash)
    {
        try
        {
            var blockResponse = await (await _cypherNetworkCore.Graph()).GetBlockAsync(hash.HexToByte());
            if (blockResponse.Block is { }) return new ObjectResult(new { blockResponse.Block });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the block height");
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <param name="skip"></param>
    /// <param name="take"></param>
    /// <returns></returns>
    [HttpGet("blocks/{skip}/{take}", Name = "GetBlocks")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBlocksAsync(int skip, int take)
    {
        Guard.Argument(skip, nameof(skip)).NotNegative();
        Guard.Argument(take, nameof(take)).NotNegative();
        try
        {
            var blocksResponse = await (await _cypherNetworkCore.Graph()).GetBlocksAsync(new BlocksRequest(skip, take));
            return new ObjectResult(new { blocksResponse.Blocks });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get blocks");
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [HttpGet("height", Name = "GetBlockHeight")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBlockHeightAsync()
    {
        try
        {
            var blockCountResponse = await (await _cypherNetworkCore.Graph()).GetBlockCountAsync();
            return new ObjectResult(new { height = blockCountResponse.Count });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the block height");
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet("transaction/{id}", Name = "GetTransaction")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionAsync(string id)
    {
        Guard.Argument(id, nameof(id)).NotNull().NotEmpty().NotWhiteSpace();
        try
        {
            var transactionResponse =
                await (await _cypherNetworkCore.Graph()).GetTransactionAsync(new TransactionRequest(id.HexToByte()));
            return new ObjectResult(new { transactionResponse.Transaction });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the transaction");
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [HttpGet("safeguards", Name = "GetSafeguardBlocks")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSafeguardBlocksAsync()
    {
        try
        {
            var safeguardBlocksResponse =
                await (await _cypherNetworkCore.Graph()).GetSafeguardBlocksAsync(new SafeguardBlocksRequest(147));
            return new ObjectResult(new { safeguardBlocksResponse.Blocks });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the safeguard blocks");
        }

        return NotFound();
    }
}