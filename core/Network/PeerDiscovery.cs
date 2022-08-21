// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetwork.Persistence;
using MessagePack;
using Microsoft.Toolkit.HighPerformance;
using NBitcoin;
using Nerdbank.Streams;
using Serilog;
using nng;
using nng.Native;

namespace CypherNetwork.Network;

public interface IPeerDiscovery
{
    /// <summary>
    /// </summary>
    /// <returns></returns>
    Peer[] GetDiscoveryStore();

    /// <summary>
    /// </summary>
    /// <returns></returns>
    LocalNode GetLocalNode();

    /// <summary>
    /// </summary>
    /// <returns></returns>
    Peer GetLocalPeer();

    /// <summary>
    /// </summary>
    /// <returns></returns>
    Task<Peer[]> GetDiscoveryAsync();

    /// <summary>
    /// </summary>
    /// <returns></returns>
    int Count();

    /// <summary>
    /// 
    /// </summary>
    void TryBootstrap();

    void SetPeerCooldown(PeerCooldown peer);
}

/// <summary>
/// </summary>
public sealed class PeerDiscovery : IDisposable, IPeerDiscovery
{
    private const int PrunedTimeoutFromSeconds = 10000;
    private const int SurveyorWaitTimeMilliseconds = 2500;
    private const int ReceiveWaitTimeMilliseconds = 1000;
    private readonly Caching<Peer> _caching = new();
    private readonly Caching<PeerCooldown> _peerCooldownCaching = new();
    private readonly ICypherNetworkCore _cypherNetworkCore;
    private readonly ILogger _logger;
    private IDisposable _discoverDisposable;
    private IDisposable _receiverDisposable;
    private IDisposable _coolDownDisposable;
    private Peer _localPeer;
    private Node[] _seedNodes;
    private ISurveyorSocket _socket;
    private ISurveyorAsyncContext<INngMsg> _ctx;
    private bool _disposed;

    private static readonly object LockOnReady = new();
    private static readonly object LockOnBootstrap = new();

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public PeerDiscovery(ICypherNetworkCore cypherNetworkCore, ILogger logger)
    {
        _cypherNetworkCore = cypherNetworkCore;
        _logger = logger;
        Init();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public int Count()
    {
        return _caching.Count;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public Peer[] GetDiscoveryStore()
    {
        return _caching.GetItems().Where(peer => _peerCooldownCaching.GetItems().All(coolDown =>
            !coolDown.Advertise.Xor(peer.Advertise) || !coolDown.PublicKey.Xor(peer.PublicKey))).ToArray();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public Task<Peer[]> GetDiscoveryAsync() => Task.FromResult(GetDiscoveryStore());

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public Peer GetLocalPeer()
    {
        return _localPeer;
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateLocalPeerInfo()
    {
        var blockCountResponse = AsyncHelper.RunSync(async delegate
        {
            var value = await (await _cypherNetworkCore.Graph()).GetBlockCountAsync();
            return value;
        });

        _localPeer.BlockCount = (ulong)blockCountResponse.Count;
        _localPeer.Timestamp = Util.GetAdjustedTimeAsUnixTimestamp();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public LocalNode GetLocalNode()
    {
        return new LocalNode
        {
            Advertise = _cypherNetworkCore.AppOptions.Gossip.Advertise,
            HttpEndPoint = _cypherNetworkCore.AppOptions.HttpEndPoint.ToBytes(),
            Identifier = _cypherNetworkCore.KeyPair.PublicKey.ToHashIdentifier(),
            Listening = _cypherNetworkCore.AppOptions.Gossip.Listening,
            Name = _cypherNetworkCore.AppOptions.Name.ToBytes(),
            PublicKey = _cypherNetworkCore.KeyPair.PublicKey,
            Version = Util.GetAssemblyVersion().ToBytes()
        };
    }

    /// <summary>
    /// </summary>
    private void Init()
    {
        var localNode = GetLocalNode();
        _localPeer = new Peer
        {
            HttpEndPoint = localNode.HttpEndPoint,
            ClientId = localNode.PublicKey.ToHashIdentifier(),
            Listening = localNode.Listening,
            Advertise = localNode.Advertise,
            Name = localNode.Name,
            PublicKey = localNode.PublicKey,
            Version = localNode.Version
        };
        _seedNodes = new Node[_cypherNetworkCore.AppOptions.Gossip.Seeds.Length];
        _cypherNetworkCore.AppOptions.Gossip.Seeds.CopyTo(_seedNodes, 0);
        DiscoverAsync().ConfigureAwait(false);
        ReceiverAsync().ConfigureAwait(false);
        HandlePeerCooldown();
    }

    /// <summary>
    /// 
    /// </summary>
    private Task DiscoverAsync()
    {
        var ipEndPoint = Util.TryParseAddress(_cypherNetworkCore.AppOptions.Gossip.Advertise[6..].FromBytes());
        Util.ThrowPortNotFree(ipEndPoint.Port);
        _socket = NngFactorySingleton.Instance.Factory.SurveyorOpen()
            .ThenListen($"tcp://{ipEndPoint.Address}:{ipEndPoint.Port}", Defines.NngFlag.NNG_FLAG_NONBLOCK).Unwrap();
        _ctx = _socket.CreateAsyncContext(NngFactorySingleton.Instance.Factory).Unwrap();
        _ctx.Ctx.SetOpt(Defines.NNG_OPT_SURVEYOR_SURVEYTIME,
            new nng_duration { TimeMs = SurveyorWaitTimeMilliseconds });
        _discoverDisposable = Observable.Timer(TimeSpan.Zero, TimeSpan.FromMilliseconds(1000)).Subscribe(_ =>
        {
            if (_cypherNetworkCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            StartWorkerAsync(_ctx).Wait();
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ctx"></param>
    private async Task StartWorkerAsync(IReceiveAsyncContext<INngMsg> ctx)
    {
        INngMsg nngMsg = default;
        try
        {
            var msg = NngFactorySingleton.Instance.Factory.CreateMessage();
            (await _ctx.Send(msg)).Unwrap();
            var nngResult = await ctx.Receive(CancellationToken.None);
            if (!nngResult.IsOk()) return;
            nngMsg = nngResult.Unwrap();
            await ReceivedPeersAsync(nngMsg);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }
        finally
        {
            nngMsg?.Dispose();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private Task ReceiverAsync()
    {
        _receiverDisposable = Observable.Timer(TimeSpan.FromMilliseconds(5000), TimeSpan.FromMilliseconds(1000)).Subscribe(t =>
        {
            if (_cypherNetworkCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            if (!Monitor.TryEnter(LockOnReady)) return;
            try
            {
                TryBootstrap();
                if (_caching.Count == 0) return;
                OnReady();
            }
            finally
            {
                Monitor.Exit(LockOnReady);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 
    /// </summary>
    private async Task BootstrapSeedsAsync()
    {
        var tasks = new List<Task>();
        var sequence = new Sequence<byte>();
        try
        {
            UpdateLocalPeerInfo();
            IList<Peer> discoveryStore = new List<Peer> { _localPeer };
            ReadOnlyPeerSequence(ref discoveryStore, ref sequence);
            for (var index = 0; index < _seedNodes.Length; index++)
            {
                var i = index;
                tasks.Add(Task.Run(async () =>
                {
                    var seedNode = _seedNodes[i];
                    var nngMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
                    try
                    {
                        var tcp = string.Create(seedNode.Advertise.Length, seedNode.Advertise.AsMemory(),
                            (chars, state) =>
                            {
                                Span<char> address = System.Text.Encoding.UTF8.GetString(state.Span).ToCharArray();
                                address.CopyTo(chars);
                            });
                        using var socket = NngFactorySingleton.Instance.Factory.RespondentOpen()
                            .ThenDial(tcp, Defines.NngFlag.NNG_FLAG_NONBLOCK).Unwrap();
                        using var ctx = socket.CreateAsyncContext(NngFactorySingleton.Instance.Factory).Unwrap();
                        ctx.Ctx.SetOpt(Defines.NNG_OPT_RECVTIMEO,
                            new nng_duration { TimeMs = ReceiveWaitTimeMilliseconds });
                        var nngResult = await ctx.Receive(CancellationToken.None);
                        if (nngResult.IsOk())
                        {
                            foreach (var memory in sequence.AsReadOnlySequence) nngMsg.Append(memory.Span);
                            await ctx.Send(nngMsg);
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
                }));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            sequence.Reset();
            sequence.Dispose();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="peers"></param>
    /// <param name="sequence"></param>
    /// <returns></returns>
    private static void ReadOnlyPeerSequence(ref IList<Peer> peers, ref Sequence<byte> sequence)
    {
        var writer = new MessagePackWriter(sequence);
        writer.WriteArrayHeader(peers.Count);
        foreach (var peer in peers)
        {
            MessagePackSerializer.Serialize(ref writer, peer);
            writer.Flush();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void OnReady()
    {
        var sequence = new Sequence<byte>();
        try
        {
            IList<Peer> discoveryStore = _caching.GetItems().ToList();
            UpdateLocalPeerInfo();
            discoveryStore.Add(_localPeer);
            ReadOnlyPeerSequence(ref discoveryStore, ref sequence);
            foreach (var peer in discoveryStore)
            {
                var nngMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
                try
                {
                    var tcp = string.Create(peer.Advertise.Length, peer.Advertise.AsMemory(), (chars, state) =>
                    {
                        Span<char> address = System.Text.Encoding.UTF8.GetString(state.Span).ToCharArray();
                        address.CopyTo(chars);
                    });
                    using var socket = NngFactorySingleton.Instance.Factory.RespondentOpen()
                        .ThenDial(tcp, Defines.NngFlag.NNG_FLAG_NONBLOCK).Unwrap();
                    using var ctx = socket.CreateAsyncContext(NngFactorySingleton.Instance.Factory).Unwrap();
                    ctx.Ctx.SetOpt(Defines.NNG_OPT_RECVTIMEO,
                        new nng_duration { TimeMs = ReceiveWaitTimeMilliseconds });
                    var nngResult = ctx.Receive(CancellationToken.None).GetAwaiter().GetResult();
                    if (nngResult.IsOk())
                    {
                        foreach (var memory in sequence.AsReadOnlySequence) nngMsg.Append(memory.Span);
                        ctx.Send(nngMsg).GetAwaiter();
                    }
                }
                catch (NngException ex)
                {
                    if (ex.Error == Defines.NngErrno.ECONNREFUSED) return;
                    _logger.Here().Error("{@Message}", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.Here().Error("{@Message}", ex.Message);
                }
                finally
                {
                    if (peer.Timestamp != 0 && Util.GetAdjustedTimeAsUnixTimestamp() >
                        peer.Timestamp + PrunedTimeoutFromSeconds)
                    {
                        _caching.Remove(peer.Advertise);
                    }

                    nngMsg.Dispose();
                }
            }
        }
        finally
        {
            sequence.Reset();
            sequence.Dispose();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="nngMsg"></param>
    private async Task ReceivedPeersAsync(INngMsgPart nngMsg)
    {
        await using var stream = Util.Manager.GetStream(nngMsg.AsSpan());
        var reader = new MessagePackStreamReader(stream);
        var length = await reader.ReadArrayHeaderAsync(CancellationToken.None);
        for (var i = 0; i < length; i++)
        {
            var elementSequence = await reader.ReadAsync(CancellationToken.None);
            if (elementSequence == null) continue;
            var peer = MessagePackSerializer.Deserialize<Peer>(elementSequence.Value);
            if (peer.ClientId == _localPeer.ClientId) continue;
#if !DEBUG
            if (!IsAcceptedAddress(peer.Advertise)) return;
            if (!IsAcceptedAddress(peer.Listening)) return;
            if (!IsAcceptedAddress(peer.HttpEndPoint)) return;      
#endif
            if (!_caching.TryGet(peer.Advertise, out var cachedPeer))
            {
                _caching.AddOrUpdate(peer.Advertise, peer);
            }
            else if (cachedPeer.BlockCount != peer.BlockCount)
            {
                _caching.AddOrUpdate(peer.Advertise, peer);
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public void TryBootstrap()
    {
        if (!Monitor.TryEnter(LockOnBootstrap)) return;
        try
        {
            var peers = GetDiscoveryStore().Enumerate();
            var count = 0;
            foreach (var node in _seedNodes)
            {
                foreach (var item in peers)
                {
                    ref var peer = ref item.Value;
                    if (peer.Advertise.AsSpan().Xor(node.Advertise.AsSpan()))
                    {
                        count++;
                    }
                }
            }

            if (!(_caching.Count == 0 | count != _seedNodes.Length)) return;
            AsyncHelper.RunSync(BootstrapSeedsAsync);
        }
        finally
        {
            Monitor.Exit(LockOnBootstrap);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="peer"></param>
    public void SetPeerCooldown(PeerCooldown peer)
    {
        if (!_peerCooldownCaching.TryGet(peer.Advertise, out _))
        {
            _peerCooldownCaching.AddOrUpdate(peer.Advertise, peer);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void HandlePeerCooldown()
    {
        _coolDownDisposable = Observable.Interval(TimeSpan.FromMinutes(30)).Subscribe(_ =>
        {
            if (_cypherNetworkCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            try
            {
                var removePeerCooldownBeforeTimestamp = Util.GetUtcNow().AddMinutes(-30).ToUnixTimestamp();
                var removePeersCooldown = AsyncHelper.RunSync(async delegate
                {
                    return await _peerCooldownCaching.WhereAsync(x =>
                        new ValueTask<bool>(x.Value.Timestamp < removePeerCooldownBeforeTimestamp));
                });
                foreach (var (key, _) in removePeersCooldown)
                    _peerCooldownCaching.Remove(key);
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                _logger.Here().Error("{@Message}", ex.Message);
            }
        });
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static bool IsAcceptedAddress(byte[] value)
    {
        try
        {
            var addy = value.FromBytes();
            var p = addy.IndexOf("//", StringComparison.Ordinal);
            var a = addy.Substring(p + 2, value.Length - p - 2);
            var b = a[..a.IndexOf(":", StringComparison.Ordinal)];
            if (IPAddress.TryParse(b, out var address))
            {
                return address.ToString() != "127.0.0.1" && address.ToString() != "0.0.0.0";
            }
        }
        catch (Exception)
        {
            // Ignore
        }

        return false;
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
            _discoverDisposable?.Dispose();
            _receiverDisposable?.Dispose();
            _socket?.Dispose();
            _ctx?.Dispose();
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