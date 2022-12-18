// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using nng;
using nng.Native;
using Serilog;

namespace CypherNetwork.Network;

/// <summary>
/// 
/// </summary>
public interface IP2PDeviceReq
{
    Task<T> SendAsync<T>(ReadOnlyMemory<byte> ipAddress, ReadOnlyMemory<byte> tcpPort, ReadOnlyMemory<byte> publicKey,
        ReadOnlyMemory<byte> value, int timeMs = 0, bool deserialize = true);
}

public class EmptyMessage { }
public class Ping { }

/// <summary>
/// 
/// </summary>
public class P2PDeviceReq : IP2PDeviceReq
{
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;
    private readonly Ping _ping = new();

    public P2PDeviceReq(ICypherSystemCore cypherSystemCore)
    {
        _cypherSystemCore = cypherSystemCore;
        using var serviceScope = cypherSystemCore.ServiceScopeFactory.CreateScope();
        _logger = serviceScope.ServiceProvider.GetService<ILogger>()?.ForContext("SourceContext", nameof(P2PDeviceReq));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ipAddress"></param>
    /// <param name="tcpPort"></param>
    /// <param name="publicKey"></param>
    /// <param name="value"></param>
    /// <param name="timeMs"></param>
    /// <param name="deserialize"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<T> SendAsync<T>(ReadOnlyMemory<byte> ipAddress, ReadOnlyMemory<byte> tcpPort, ReadOnlyMemory<byte> publicKey,
        ReadOnlyMemory<byte> value, int timeMs = 0, bool deserialize = true)
    {
        var nngMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
        try
        {
            var address = string.Create(ipAddress.Length, ipAddress, (chars, state) =>
            {
                Span<char> address = System.Text.Encoding.UTF8.GetString(state.Span).ToCharArray();
                address.CopyTo(chars);
            });
            var port = string.Create(tcpPort.Length, tcpPort, (chars, state) =>
            {
                Span<char> port = System.Text.Encoding.UTF8.GetString(state.Span).ToCharArray();
                port.CopyTo(chars);
            });
            using var socket = NngFactorySingleton.Instance.Factory.RequesterOpen()
                .ThenDial($"tcp://{address}:{port}", Defines.NngFlag.NNG_FLAG_NONBLOCK).Unwrap();

            if (timeMs != 0)
            {
                socket.SetOpt(Defines.NNG_OPT_RECVTIMEO, new nng_duration { TimeMs = timeMs });
                socket.SetOpt(Defines.NNG_OPT_SENDTIMEO, new nng_duration { TimeMs = timeMs });
            }

            using var ctx = socket.CreateAsyncContext(NngFactorySingleton.Instance.Factory).Unwrap();
            var cipher = _cypherSystemCore.Crypto().BoxSeal(value.Span, publicKey.Span[1..33]);

            await using var packetStream = Util.Manager.GetStream() as RecyclableMemoryStream;
            packetStream.Write(_cypherSystemCore.KeyPair.PublicKey[1..33].WrapLengthPrefix());
            packetStream.Write(cipher.WrapLengthPrefix());
            foreach (var memory in packetStream.GetReadOnlySequence()) nngMsg.Append(memory.Span);

            var nngResult = await ctx.Send(nngMsg);
            if (!nngResult.IsOk()) return default;
            if (typeof(T) == typeof(EmptyMessage)) return default;
            if (typeof(T) == typeof(Ping)) return (T)(object)_ping;
            var nngRecvMsg = nngResult.Unwrap();
            var message = await _cypherSystemCore.P2PDevice().DecryptAsync(nngRecvMsg);
            nngRecvMsg.Dispose();
            if (message.Memory.IsEmpty)
            {
                return default;
            }
            if (!deserialize)
            {
                return (T)(object)message;
            }

            using var stream = Util.Manager.GetStream(message.Memory.Span);
            var data = await MessagePackSerializer.DeserializeAsync<T>(stream);
            return data;
        }
        catch (NngException ex)
        {
            if (ex.Error == Defines.NngErrno.ECONNREFUSED) return default;
            if (ex.Error != Defines.NngErrno.EPROTO)
            {
                _logger.Here().Error("{@Message}", ex.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }
        finally
        {
            nngMsg.Dispose();
        }

        return default;
    }
}