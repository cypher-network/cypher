// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using Dawn;
using MessagePack;
using nng;
using Serilog;
using Block = CypherNetwork.Models.Block;

namespace CypherNetwork.Ledger;

/// <summary>
/// </summary>
public interface ISync
{
    bool Running { get; }
    Task SynchronizeAsync();
}

/// <summary>
/// </summary>
public class Sync : ISync, IDisposable
{
    private const uint SyncEveryFromMinutes = 60;
    private const int RetryCount = 6;
    private readonly ICypherNetworkCore _cypherNetworkCore;
    private readonly ILogger _logger;

    private bool _disposed;
    private IDisposable _disposableInit;

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public Sync(ICypherNetworkCore cypherNetworkCore, ILogger logger)
    {
        _cypherNetworkCore = cypherNetworkCore;
        _logger = logger.ForContext("SourceContext", nameof(Sync));
        Init();
    }

    public bool Running { get; private set; }

    /// <summary>
    /// </summary>
    private void Init()
    {
        _disposableInit = Observable.Timer(TimeSpan.Zero, TimeSpan.FromMinutes(SyncEveryFromMinutes)).Subscribe(_ =>
        {
            if (_cypherNetworkCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            try
            {
                SynchronizeAsync().Wait();
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
    public async Task SynchronizeAsync()
    {
        if (Running) return;
        _logger.Information("Begin... [SYNCHRONIZATION]");
        try
        {
            Running = true;
            var blockCountResponse = await (await _cypherNetworkCore.Graph()).GetBlockCountAsync();
            _logger.Information("OPENING block height [{@Height}]", blockCountResponse.Count);
            var currentRetry = 0;
            for (; ; )
            {
                if (_cypherNetworkCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
                var hasAny = await WaitForPeersAsync(currentRetry, RetryCount);
                if (hasAny) break;
                currentRetry++;
            }

            var peers = (await _cypherNetworkCore.PeerDiscovery()).GetDiscoveryStore();
            var synchronized = false;
            foreach (var peer in peers)
            {
                blockCountResponse = await (await _cypherNetworkCore.Graph()).GetBlockCountAsync();
                if (blockCountResponse.Count >= (long)peer.BlockCount)
                {
                    var localNode = (await _cypherNetworkCore.PeerDiscovery()).GetLocalNode();
                    _logger.Information(
                        "[LOCAL node:({@LocalNodeId}) block height: ({@LocalHeight})] > or = [REMOTE node:({@RemoteNodeId}) block height: ({@RemoteBlockHeight})])",
                        localNode.Identifier.ToString(), blockCountResponse.Count.ToString(),
                        peer.ClientId.ToString(), peer.BlockCount.ToString());
                    _logger.Information("[CONTINUE]");
                    continue;
                }

                var skip = blockCountResponse.Count - 6; // +- Depth of blocks to compare.
                skip = skip < 0 ? blockCountResponse.Count : skip;
                synchronized = await SynchronizeAsync(peer, (ulong)skip, (int)peer.BlockCount);
                if (!synchronized) continue;
                _logger.Information("Successfully SYNCHRONIZED with {@Host}", peer.Listening.FromBytes());
                break;
            }

            if (!synchronized)
            {
                _logger.Warning("Unable to SYNCHRONIZE WITH REMOTE PEER(S)");
                blockCountResponse = await (await _cypherNetworkCore.Graph()).GetBlockCountAsync();
                _logger.Information("LOCAL NODE block height: [{@LocalHeight}]", blockCountResponse.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while checking");
        }
        finally
        {
            Running = false;
            _logger.Information("End... [SYNCHRONIZATION]");
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
        var discovery = await _cypherNetworkCore.PeerDiscovery();
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
            var validator = _cypherNetworkCore.Validator();
            var blocks = await FetchBlocksAsync(peer, skip, take);
            if (blocks?.Any() != true) return false;
            if (skip == 0)
            {
                _logger.Warning("FIRST TIME BOOTSTRAPPING");
            }
            else
            {
                _logger.Information("CONTINUE BOOTSTRAPPING");
                _logger.Information("CHECKING [FORK RULE]");
                var forkRuleBlocks = await validator.VerifyForkRuleAsync(blocks.OrderBy(x => x.Height).ToArray());
                if (forkRuleBlocks.Length == 0)
                {
                    _logger.Information("Fork rule check [UNABLE TO VERIFY]");
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
                    var saveBlockResponse = await (await _cypherNetworkCore.Graph()).SaveBlockAsync(new SaveBlockRequest(block));
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
            var blockCountResponse = await (await _cypherNetworkCore.Graph()).GetBlockCountAsync();
            _logger.Information("Local node block height set to ({@LocalHeight})", blockCountResponse.Count);
            if (blockCountResponse.Count == take) isSynchronized = true;
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
        _logger.Information("Synchronizing with {@Host} ({@Skip})/({@Take})", peer.Listening.FromBytes(), skip, take);
        try
        {
            _logger.Information("Fetching [{@Range}] block(s)", Math.Abs(take - (int)skip));
            using var reqSocket = NngFactorySingleton.Instance.Factory.RequesterOpen()
                .ThenDial(peer.Listening.FromBytes()).Unwrap();
            using var ctx = reqSocket.CreateAsyncContext(NngFactorySingleton.Instance.Factory).Unwrap();
            var cipher = _cypherNetworkCore.Crypto().BoxSeal(
                MessagePackSerializer.Serialize(new Parameter[]
                {
                    new() { Value = skip.ToBytes(), ProtocolCommand = ProtocolCommand.GetBlocks },
                    new() { Value = take.ToBytes(), ProtocolCommand = ProtocolCommand.GetBlocks }
                }), peer.PublicKey.AsSpan()[1..33]);
            var packet = Util.Combine(_cypherNetworkCore.KeyPair.PublicKey[1..33].WrapLengthPrefix(),
                cipher.WrapLengthPrefix());
            var nngSendMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
            nngSendMsg.Append(packet.AsSpan());
            var nngResult = await ctx.Send(nngSendMsg);
            nngSendMsg.Dispose();
            if (nngResult.IsOk())
            {
                var nngRecvMsg = nngResult.Unwrap();
                var message = await _cypherNetworkCore.P2PDevice().DecryptAsync(nngRecvMsg);
                nngRecvMsg.Dispose();
                if (message.Memory.IsEmpty)
                {
                    throw new Exception("Failed to decrypt the data");
                }

                await using var stream = Util.Manager.GetStream(message.Memory.Span);
                var blocksResponse = await MessagePackSerializer.DeserializeAsync<BlocksResponse>(stream);
                _logger.Information("Finished with [{@BlockCount}] block(s)", blocksResponse.Blocks.Count);
                return blocksResponse.Blocks;
            }
        }
        catch (NngException ex)
        {
            _logger.Warning("Dead message {@Peer} {@Message}", peer.Listening.FromBytes(), ex.Message);
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