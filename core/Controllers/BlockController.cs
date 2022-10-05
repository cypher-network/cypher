// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
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
    private readonly ICypherSystemCore _cypherNetworkCore;
    private readonly ILogger _logger;

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public BlockController(ICypherSystemCore cypherNetworkCore, ILogger logger)
    {
        _cypherNetworkCore = cypherNetworkCore;
        _logger = logger.ForContext("SourceContext", nameof(BlockController));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    [HttpGet("supply", Name = "GetSupply")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSupply()
    {
        try
        {
            var distribution = await _cypherNetworkCore.Validator().GetRunningDistributionAsync();
            return new ObjectResult(new { distribution });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the supply");
        }

        return NotFound();
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
            var blockResponse =
                await _cypherNetworkCore.Graph().GetBlockAsync(new BlockRequest(hash.HexToByte()));
            if (blockResponse?.Block is { }) return new ObjectResult(new { blockResponse.Block });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the block");
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
            var blocksResponse =
                await _cypherNetworkCore.Graph().GetBlocksAsync(new BlocksRequest(skip, take));
            return new ObjectResult(new { blocksResponse?.Blocks });
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
    [HttpGet("block/{height}", Name = "GetBlockByHeight")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBlockByHeightAsync(ulong height)
    {
        try
        {
            var blockResponse =
                await _cypherNetworkCore.Graph().GetBlockByHeightAsync(new BlockByHeightRequest(height));
            if (blockResponse?.Block is { }) return new ObjectResult(new { blockResponse.Block });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the block");
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <param name="hash"></param>
    /// <returns></returns>
    [HttpGet("block/transaction/{hash}", Name = "GetTransactionBlock")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionBlockAsync(string hash)
    {
        Guard.Argument(hash, nameof(hash)).NotNull().NotEmpty().NotWhiteSpace();
        try
        {
            var transactionBlock =
                await _cypherNetworkCore.Graph().GetTransactionBlockAsync(
                    new TransactionIdRequest(hash.HexToByte()));
            return new ObjectResult(new { transactionBlock?.Block });
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
    [HttpGet("height", Name = "GetBlockHeight")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBlockHeightAsync()
    {
        try
        {
            var blockCountResponse =
                await _cypherNetworkCore.Graph().GetBlockCountAsync();
            return new ObjectResult(new { height = blockCountResponse?.Count });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the block height");
        }

        return NotFound();
    }

    /// <summary>
    /// </summary>
    /// <param name="hash"></param>
    /// <returns></returns>
    [HttpGet("transaction/{hash}", Name = "GetTransaction")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionAsync(string hash)
    {
        Guard.Argument(hash, nameof(hash)).NotNull().NotEmpty().NotWhiteSpace();
        try
        {
            var transactionResponse =
                await _cypherNetworkCore.Graph().GetTransactionAsync(new TransactionRequest(hash.HexToByte()));
            return new ObjectResult(new { transactionResponse?.Transaction });
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
                await _cypherNetworkCore.Graph().GetSafeguardBlocksAsync(new SafeguardBlocksRequest(147));
            return new ObjectResult(new { safeguardBlocksResponse?.Blocks });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get safeguard blocks");
        }

        return NotFound();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    [HttpGet("emission", Name = "GetRunningDistribution")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRunningDistributionAsync()
    {
        try
        {
            var distribution = await _cypherNetworkCore.Validator().GetRunningDistributionAsync();
            return new ObjectResult(new { emission = Ledger.LedgerConstant.Distribution - distribution });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to get the emission");
        }

        return NotFound();
    }
}