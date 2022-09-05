// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using MessagePack;
using nng;
using nng.Native;
using Serilog;

namespace CypherNetwork.Network;

public interface IBroadcast
{
    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    Task PublishAsync((TopicType, byte[]) value);
}

/// <summary>
/// </summary>
public class Broadcast : IBroadcast
{
    private readonly ActionBlock<(TopicType, byte[])> _action;
    private readonly ICypherNetworkCore _cypherNetworkCore;
    private readonly ILogger _logger;

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public Broadcast(ICypherNetworkCore cypherNetworkCore, ILogger logger)
    {
        _cypherNetworkCore = cypherNetworkCore;
        _logger = logger.ForContext("SourceContext", nameof(Broadcast));
        var dataflowBlockOptions = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = 10,
            EnsureOrdered = true
        };
        _action = new ActionBlock<(TopicType, byte[])>(BroadcastQueueOnReceiveReadyAsync, dataflowBlockOptions);
    }

    /// <summary>
    /// </summary>
    /// <param name="values"></param>
    public async Task PublishAsync((TopicType, byte[]) values)
    {
        await _action.SendAsync(values);
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    private async Task BroadcastQueueOnReceiveReadyAsync((TopicType, byte[]) value)
    {
        try
        {
            var (topicType, data) = value;
            var peers = await (await _cypherNetworkCore.PeerDiscovery()).GetDiscoveryAsync();
            if (peers.Any())
            {
                var command = topicType switch
                {
                    TopicType.AddTransaction => ProtocolCommand.Transaction,
                    _ => ProtocolCommand.BlockGraph
                };
                var msg = MessagePackSerializer.Serialize(new Parameter[]
                {
                    new() { ProtocolCommand = command, Value = data }
                });
                await Parallel.ForEachAsync(peers,  (knownPeer, cancellationToken) =>
                {
                    var nngMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
                    try
                    {
                        if (cancellationToken.IsCancellationRequested) return ValueTask.CompletedTask;
                        var tcp = string.Create(knownPeer.Listening.Length, knownPeer.Listening.AsMemory(),
                            (chars, state) =>
                            {
                                Span<char> address = System.Text.Encoding.UTF8.GetString(state.Span).ToCharArray();
                                address.CopyTo(chars);
                            });
                        using var reqSocket =
                            NngFactorySingleton.Instance.Factory.RequesterOpen().ThenDial(tcp).Unwrap();
                        reqSocket.SetOpt(Defines.NNG_OPT_RECVTIMEO, new nng_duration { TimeMs = 200 });
                        reqSocket.SetOpt(Defines.NNG_OPT_SENDTIMEO, new nng_duration { TimeMs = 200 });
                        var cipher = _cypherNetworkCore.Crypto().BoxSeal(msg, knownPeer.PublicKey.AsSpan()[1..33]);
                        var packet = Util.Combine(_cypherNetworkCore.KeyPair.PublicKey[1..33].WrapLengthPrefix(),
                            cipher.WrapLengthPrefix());
                        nngMsg.Append(packet.AsSpan());
                        reqSocket.SendMsg(nngMsg, Defines.NngFlag.NNG_FLAG_NONBLOCK);
                    }
                    catch (Exception)
                    {
                        // _logger.Here().Error("Peer: {@Peer} failed to send {@Topic} with {@Message}",
                        //     knownPeer.Listening, command, ex.Message);
                    }
                    finally
                    {
                        nngMsg.Dispose();
                    }
                    
                    return ValueTask.CompletedTask;
                });
            }
            else
            {
                _logger.Warning("Broadcast no peers");
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }
    }
}