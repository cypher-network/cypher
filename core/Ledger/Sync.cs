// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using Dawn;
using MessagePack;
using Serilog;
using Block = CypherNetwork.Models.Block;

namespace CypherNetwork.Ledger;

/// <summary>
/// </summary>
public interface ISync
{
    bool Running { get; }
}

/// <summary>
/// </summary>
public class Sync : ISync, IDisposable
{
    private const int RetryCount = 6;
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;
    private IDisposable _disposableInit;

    private bool _disposed;
    private int _running;

    /// <summary>
    /// </summary>
    /// <param name="cypherSystemCore"></param>
    /// <param name="logger"></param>
    public Sync(ICypherSystemCore cypherSystemCore, ILogger logger)
    {
        _cypherSystemCore = cypherSystemCore;
        _logger = logger.ForContext("SourceContext", nameof(Sync));
        Init();
    }

    public bool Running => _running != 0;

    /// <summary>
    /// </summary>
    private void Init()
    {
        _disposableInit = Observable.Timer(TimeSpan.Zero, TimeSpan.FromMinutes(_cypherSystemCore.Node.Network.AutoSyncEveryMinutes)).Subscribe(_ =>
        {
            if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            try
            {
                if (Running) return;
                _running = 1;
                SynchronizeAsync().SafeFireAndForget(exception =>
                {
                    _logger.Here().Error("{@Message}", exception.Message);
                });
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
        });
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task SynchronizeAsync()
    {
        _logger.Information("Begin... [SYNCHRONIZATION]");
        try
        {
            var blockCountResponse =
                await _cypherSystemCore.Graph().GetBlockCountAsync();
            _logger.Information("OPENING block height [{@Height}]", blockCountResponse?.Count);
            var currentRetry = 0;
            for (; ; )
            {
                if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
                var hasAny = await WaitForPeersAsync(currentRetry, RetryCount);
                if (hasAny) break;
                currentRetry++;
            }

            foreach (var peer in _cypherSystemCore.PeerDiscovery().GetDiscoveryStore())
            {
                if (blockCountResponse?.Count < (long)peer.BlockCount)
                {
                    var skip = blockCountResponse.Count - 6; // +- Depth of blocks to compare.
                    skip = skip < 0 ? blockCountResponse.Count : skip;
                    var synchronized = await SynchronizeAsync(peer, (ulong)skip, (int)peer.BlockCount);
                    if (!synchronized) continue;
                    _logger.Information(
                        "Successfully SYNCHRONIZED with node:{@NodeName} host:{@Host} version:{@Version}",
                        peer.Name.FromBytes(), peer.IpAddress.FromBytes(), peer.Version.FromBytes());
                    break;
                }

                blockCountResponse = await _cypherSystemCore.Graph().GetBlockCountAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while checking");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            var blockCountResponse = await _cypherSystemCore.Graph().GetBlockCountAsync();
            _logger.Information("LOCAL NODE block height: [{@LocalHeight}]", blockCountResponse?.Count);
            _logger.Information("End... [SYNCHRONIZATION]");
            _logger.Information("Next...[SYNCHRONIZATION] in {@Message} minute(s)",
                _cypherSystemCore.Node.Network.AutoSyncEveryMinutes);
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="currentRetry"></param>
    /// <param name="retryCount"></param>
    /// <returns></returns>
    private async Task<bool> WaitForPeersAsync(int currentRetry, int retryCount)
    {
        Guard.Argument(currentRetry, nameof(currentRetry)).NotNegative();
        Guard.Argument(retryCount, nameof(retryCount)).NotNegative();
        var jitter = new Random();
        var discovery = _cypherSystemCore.PeerDiscovery();
        if (discovery.Count() != 0) return true;
        if (currentRetry >= retryCount || discovery.Count() != 0) return true;
        var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, currentRetry)) +
                         TimeSpan.FromMilliseconds(jitter.Next(0, 1000));
        _logger.Warning("Waiting for peers... [RETRYING] in {@Sec}s", retryDelay.Seconds);
        await Task.Delay(retryDelay);
        return false;
    }

    /// <summary>
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="skip"></param>
    /// <param name="take"></param>
    /// <returns></returns>
    private async Task<bool> SynchronizeAsync(Peer peer, ulong skip, int take)
    {
        Guard.Argument(peer, nameof(peer)).HasValue();
        Guard.Argument(skip, nameof(skip)).NotNegative();
        Guard.Argument(take, nameof(take)).NotNegative();
        var isSynchronized = false;
        try
        {
            var validator = _cypherSystemCore.Validator();
            var blocks = await FetchBlocksAsync(peer, skip, take);
            if (blocks?.Any() != true) return false;
            if (skip == 0)
            {
                _logger.Warning("FIRST TIME BOOTSTRAPPING");
            }
            else
            {
                _logger.Information("CONTINUE BOOTSTRAPPING");
                _logger.Information("CHECKING [BLOCK HEIGHTS]");

                var verifyNoDuplicateBlockHeights = validator.VerifyNoDuplicateBlockHeights(blocks);
                if (verifyNoDuplicateBlockHeights == VerifyResult.AlreadyExists)
                {
                    _cypherSystemCore.PeerDiscovery().SetPeerCooldown(new PeerCooldown
                    { IpAddress = peer.IpAddress, PublicKey = peer.PublicKey });
                    _logger.Warning("Duplicate block heights [UNABLE TO VERIFY]");
                    return false;
                }

                _logger.Information("CHECKING [FORK RULE]");
                var forkRuleBlocks = await validator.VerifyForkRuleAsync(blocks.OrderBy(x => x.Height).ToArray());
                if (forkRuleBlocks.Length == 0)
                {
                    _logger.Fatal("Fork rule check [UNABLE TO VERIFY]");
                    return false;
                }

                blocks = forkRuleBlocks.ToList();
                _logger.Information("Fork rule check [OK]");
            }

            _logger.Information("SYNCHRONIZING [{@BlockCount}] Block(s)", blocks.Count);
            foreach (var block in blocks.OrderBy(x => x.Height))
                try
                {
                    _logger.Information("SYNCING block height: [{@Height}]", block.Height);
                    var verifyBlockHeader = await validator.VerifyBlockAsync(block);
                    if (verifyBlockHeader != VerifyResult.Succeed) return false;
                    _logger.Information("SYNCHRONIZED [OK]");
                    var saveBlockResponse = await _cypherSystemCore.Graph().SaveBlockAsync(new SaveBlockRequest(block));
                    if (saveBlockResponse.Ok) continue;
                    _logger.Error("Unable to save block: {@Hash}", block.Hash);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Unable to save block: {@Hash}", block.Hash.ByteToHex());
                    return false;
                }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "SYNCHRONIZATION [FAILED]");
            return false;
        }
        finally
        {
            var blockCountResponse = await _cypherSystemCore.Graph().GetBlockCountAsync();
            _logger.Information("Local node block height set to ({@LocalHeight})", blockCountResponse?.Count);
            if (blockCountResponse?.Count == take) isSynchronized = true;
        }

        return isSynchronized;
    }

    /// <summary>
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="skip"></param>
    /// <param name="take"></param>
    /// <returns></returns>
    private async Task<IReadOnlyList<Block>> FetchBlocksAsync(Peer peer, ulong skip, int take)
    {
        Guard.Argument(peer, nameof(peer)).HasValue();
        Guard.Argument(skip, nameof(skip)).NotNegative();
        Guard.Argument(take, nameof(take)).NotNegative();
        _logger.Information("Synchronizing with {@Host} ({@Skip})/({@Take})", peer.IpAddress.FromBytes(), skip, take);
        try
        {
            _logger.Information("Fetching [{@Range}] block(s)", Math.Abs(take - (int)skip));
            var blocksResponse = await _cypherSystemCore.P2PDeviceReq().SendAsync<BlocksResponse>(peer.IpAddress,
                peer.TcpPort, peer.PublicKey,
                MessagePackSerializer.Serialize(new Parameter[]
                {
                    new() { Value = skip.ToBytes(), ProtocolCommand = ProtocolCommand.GetBlocks },
                    new() { Value = take.ToBytes(), ProtocolCommand = ProtocolCommand.GetBlocks }
                }));
            _logger.Information("Finished with [{@BlockCount}] block(s)", blocksResponse.Blocks.Count);
            return blocksResponse.Blocks;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="disposing"></param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _disposableInit?.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// 
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}