using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models.Messages;
using MessagePack;
using Microsoft.IO;
using nng;
using Serilog;

namespace CypherNetwork.Network;

/// <summary>
/// 
/// </summary>
public class P2PDeviceWorker : ReceivedActor<INngMsg>
{
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly IRepReqAsyncContext<INngMsg> _ctx;
    private readonly ILogger _logger;
    private readonly AutoResetEvent _autoReset = new(false);
    public P2PDeviceWorker(ICypherSystemCore cypherSystemCore, IRepReqAsyncContext<INngMsg> ctx, ILogger logger)
        : base(new ExecutionDataflowBlockOptions { BoundedCapacity = 1, MaxDegreeOfParallelism = 1 })
    {
        _cypherSystemCore = cypherSystemCore;
        _ctx = ctx;
        _logger = logger;
    }

    /// <summary>
    /// 
    /// </summary>
    public async Task WorkerAsync()
    {
        var nngResult = await _ctx.Receive();
        await PostAsync(nngResult.Unwrap());
        _autoReset.WaitOne(10000);
        _autoReset.Close();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="nngMsg"></param>
    protected override async Task OnReceiveAsync(INngMsg nngMsg)
    {
        try
        {
            var message = await _cypherSystemCore.P2PDevice().DecryptAsync(nngMsg);
            if (message.Memory.Length == 0)
            {
                await EmptyReplyAsync();
                return;
            }

            var unwrapMessage = await P2PDevice.UnWrapAsync(message.Memory);
            if (unwrapMessage.ProtocolCommand != ProtocolCommand.NotFound)
            {
                var newMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
                try
                {
                    var response =
                        await _cypherSystemCore.P2PDeviceApi().Commands[(int)unwrapMessage.ProtocolCommand](
                            unwrapMessage.Parameters);
                    if (unwrapMessage.ProtocolCommand == ProtocolCommand.UpdatePeers)
                    {
                        await EmptyReplyAsync();
                        return;
                    }

                    var cipher = _cypherSystemCore.Crypto().BoxSeal(
                        response.IsSingleSegment ? response.First.Span : response.ToArray(), message.PublicKey);
                    if (cipher.Length != 0)
                    {
                        await using var packetStream = Util.Manager.GetStream() as RecyclableMemoryStream;
                        packetStream.Write(_cypherSystemCore.KeyPair.PublicKey[1..33].WrapLengthPrefix());
                        packetStream.Write(cipher.WrapLengthPrefix());
                        foreach (var memory in packetStream.GetReadOnlySequence()) newMsg.Append(memory.Span);
                        (await _ctx.Reply(newMsg)).Unwrap();
                        return;
                    }
                }
                catch (MessagePackSerializationException)
                {
                    // Ignore
                }
                catch (AccessViolationException ex)
                {
                    _logger.Here().Fatal("{@Message}", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.Here().Fatal("{@Message}", ex.Message);
                }
                finally
                {
                    newMsg.Dispose();
                }
            }

            await EmptyReplyAsync();
        }
        finally
        {
            _autoReset.Set();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private async Task EmptyReplyAsync()
    {
        try
        {
            var newMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
            (await _ctx.Reply(newMsg)).Unwrap();
        }
        catch (Exception)
        {
            // Ignore
        }
    }
}