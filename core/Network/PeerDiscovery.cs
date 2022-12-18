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
    private const int PrunedTimeoutFromSeconds = 120;
    private const int SurveyorWaitTimeMilliseconds = 2500;
    private const int ReceiveWaitTimeMilliseconds = 1000;
    private readonly Caching<Peer> _caching = new();
    private readonly Caching<PeerCooldown> _peerCooldownCaching = new();
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;
    private IDisposable _discoverDisposable;
    private IDisposable _receiverDisposable;
    private IDisposable _coolDownDisposable;
    private LocalNode _localNode;
    private Peer _localPeer;
    private RemoteNode[] _seedNodes;
    private ISurveyorSocket _socket;
    private ISurveyorAsyncContext<INngMsg> _ctx;
    private bool _disposed;

    private static readonly object LockOnReady = new();
    private static readonly object LockOnBootstrap = new();

    /// <summary>
    /// </summary>
    /// <param name="cypherNetworkCore"></param>
    /// <param name="logger"></param>
    public PeerDiscovery(ICypherSystemCore cypherNetworkCore, ILogger logger)
    {
        _cypherSystemCore = cypherNetworkCore;
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
        var peers = _caching.GetItems();
        return peers.Where(peer => _peerCooldownCaching.GetItems().All(coolDown =>
            !coolDown.IpAddress.Xor(peer.IpAddress.AsSpan()) || !coolDown.PublicKey.Xor(peer.PublicKey.AsSpan()))).ToArray();
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
    /// <summary>
    /// 
    /// </summary>
    private void UpdateLocalPeerInfo()
    {
        _localPeer.BlockCount = _cypherSystemCore.UnitOfWork().HashChainRepository.Count;
        _localPeer.Timestamp = Util.GetAdjustedTimeAsUnixTimestamp();
        _localPeer.Signature = _cypherSystemCore.Crypto().Sign(
            _cypherSystemCore.KeyPair.PrivateKey.FromSecureString().HexToByte(), _localPeer.Timestamp.ToBytes());
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public LocalNode GetLocalNode()
    {
        return _localNode;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="clientId"></param>
    /// <param name="ipAddress"></param>
    /// <param name="peer"></param>
    private void UpdatePeer(ulong clientId, byte[] ipAddress, Peer peer)
    {
        _caching.AddOrUpdate(StoreDb.Key(clientId.ToString(), ipAddress), peer);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="clientId"></param>
    /// <param name="ipAddress"></param>
    /// <returns></returns>
    private static byte[] GetKey(ulong clientId, byte[] ipAddress)
    {
        return StoreDb.Key(clientId.ToString(), ipAddress);
    }

    /// <summary>
    /// </summary>
    private void Init()
    {
        _localNode = new LocalNode
        {
            IpAddress = _cypherSystemCore.Node.EndPoint.Address.ToString().ToBytes(),
            Identifier = _cypherSystemCore.KeyPair.PublicKey.ToHashIdentifier(),
            TcpPort = _cypherSystemCore.Node.Network.P2P.TcpPort.ToBytes(),
            WsPort = _cypherSystemCore.Node.Network.P2P.WsPort.ToBytes(),
            DsPort = _cypherSystemCore.Node.Network.P2P.DsPort.ToBytes(),
            HttpPort = _cypherSystemCore.Node.Network.HttpPort.ToBytes(),
            HttpsPort = _cypherSystemCore.Node.Network.HttpsPort.ToBytes(),
            Name = _cypherSystemCore.Node.Name.ToBytes(),
            PublicKey = _cypherSystemCore.KeyPair.PublicKey,
            Version = Util.GetAssemblyVersion().ToBytes()
        };
        _localPeer = new Peer
        {
            IpAddress = _localNode.IpAddress,
            HttpPort = _localNode.HttpPort,
            HttpsPort = _localNode.HttpsPort,
            ClientId = _localNode.PublicKey.ToHashIdentifier(),
            TcpPort = _localNode.TcpPort,
            WsPort = _localNode.WsPort,
            DsPort = _localNode.DsPort,
            Name = _localNode.Name,
            PublicKey = _localNode.PublicKey,
            Version = _localNode.Version
        };
        _seedNodes = new RemoteNode[_cypherSystemCore.Node.Network.SeedList.Count];
        foreach (var seedNode in _cypherSystemCore.Node.Network.SeedList.WithIndex())
        {
            var endpoint = Util.GetIpEndPoint(seedNode.item);
            _seedNodes[seedNode.index] = new RemoteNode(endpoint.Address.ToString().ToBytes(), endpoint.Port.ToBytes(), null);
        }
        DiscoverAsync().ConfigureAwait(false);
        ReceiverAsync().ConfigureAwait(false);
        HandlePeerCooldown();
    }

    /// <summary>
    /// 
    /// </summary>
    private Task DiscoverAsync()
    {
        Util.ThrowPortNotFree(_cypherSystemCore.Node.Network.P2P.DsPort);
        var ipEndPoint = new IPEndPoint(_cypherSystemCore.Node.EndPoint.Address,
            _cypherSystemCore.Node.Network.P2P.DsPort);
        _socket = NngFactorySingleton.Instance.Factory.SurveyorOpen()
            .ThenListen($"tcp://{ipEndPoint.Address}:{ipEndPoint.Port}", Defines.NngFlag.NNG_FLAG_NONBLOCK).Unwrap();
        _socket.SetOpt(Defines.NNG_OPT_RECVMAXSZ, 5000000);
        _ctx = _socket.CreateAsyncContext(NngFactorySingleton.Instance.Factory).Unwrap();
        _ctx.Ctx.SetOpt(Defines.NNG_OPT_SURVEYOR_SURVEYTIME,
            new nng_duration { TimeMs = SurveyorWaitTimeMilliseconds });
        _discoverDisposable = Observable.Timer(TimeSpan.Zero, TimeSpan.FromMilliseconds(1000)).Subscribe(_ =>
        {
            if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
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
            if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            if (!Monitor.TryEnter(LockOnReady)) return;
            try
            {
                if (_seedNodes.Length != 0) TryBootstrap();
                if (_caching.Count == 0) return;
                OnReadyAsync().Wait();
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
                        await BroadcastAsync(seedNode.IpAddress, seedNode.TcpPort, sequence, nngMsg);
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
    private async Task OnReadyAsync()
    {
        var sequence = new Sequence<byte>();
        try
        {
            IList<Peer> discoveryStore = _caching.GetItems().ToList();
            UpdateLocalPeerInfo();
            discoveryStore.Add(_localPeer);
            ReadOnlyPeerSequence(ref discoveryStore, ref sequence);
            for (var index = 0; index < discoveryStore.Count; index++)
            {
                var peer = discoveryStore[index];
                if (peer.ClientId == _localPeer.ClientId) continue;
                var nngMsg = NngFactorySingleton.Instance.Factory.CreateMessage();
                try
                {
                    await BroadcastAsync(peer.IpAddress, peer.DsPort, sequence, nngMsg);
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
                    if (peer.Timestamp < Util.GetUtcNow().AddSeconds(-PrunedTimeoutFromSeconds).ToUnixTimestamp())
                    {
                        SetPeerCooldown(new PeerCooldown
                        {
                            IpAddress = peer.IpAddress,
                            PublicKey = peer.PublicKey,
                            ClientId = peer.ClientId,
                            PeerState = PeerState.Unreachable
                        });
                        _caching.Remove(GetKey(peer.ClientId, peer.IpAddress));
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
    /// <param name="ipAddress"></param>
    /// <param name="dsPort"></param>
    /// <param name="sequence"></param>
    /// <param name="nngMsg"></param>
    private static async Task BroadcastAsync(byte[] ipAddress, byte[] dsPort, Sequence<byte> sequence, INngMsg nngMsg)
    {
        var address = string.Create(ipAddress.Length, ipAddress.AsMemory(), (chars, state) =>
        {
            Span<char> address = System.Text.Encoding.UTF8.GetString(state.Span).ToCharArray();
            address.CopyTo(chars);
        });
        var port = string.Create(dsPort.Length, dsPort.AsMemory(), (chars, state) =>
        {
            Span<char> port = System.Text.Encoding.UTF8.GetString(state.Span).ToCharArray();
            port.CopyTo(chars);
        });
        using var socket = NngFactorySingleton.Instance.Factory.RespondentOpen()
            .ThenDial($"tcp://{address}:{port}", Defines.NngFlag.NNG_FLAG_NONBLOCK).Unwrap();
        using var ctx = socket.CreateAsyncContext(NngFactorySingleton.Instance.Factory).Unwrap();
        ctx.Ctx.SetOpt(Defines.NNG_OPT_RECVTIMEO,
            new nng_duration { TimeMs = ReceiveWaitTimeMilliseconds });
        var nngResult = await ctx.Receive(CancellationToken.None);
        if (!nngResult.IsOk()) return;
        foreach (var memory in sequence.AsReadOnlySequence) nngMsg.Append(memory.Span);
        await ctx.Send(nngMsg);
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
            if (!IsAcceptedAddress(peer.IpAddress)) return;
#endif
            if (!_cypherSystemCore.Crypto()
                    .VerifySignature(peer.PublicKey, peer.Timestamp.ToBytes(), peer.Signature)) continue;
            var key = GetKey(peer.ClientId, peer.IpAddress);
            if (!_caching.TryGet(key, out var cachedPeer) && _peerCooldownCaching[key].IsDefault())
            {
                UpdatePeer(peer.ClientId, peer.IpAddress, peer);
                continue;
            }

            if (!cachedPeer.IsDefault())
            {
                if (cachedPeer.Timestamp >= peer.Timestamp) continue;
                // ReSharper disable once RedundantAssignment
                cachedPeer = peer;
                continue;
            }

            if (_peerCooldownCaching[key].IsDefault()) continue;
            if (_peerCooldownCaching[key].Timestamp >= peer.Timestamp) continue;
            _peerCooldownCaching.Remove(key);
            UpdatePeer(peer.ClientId, peer.IpAddress, peer);
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
                    if (peer.IpAddress.AsSpan().Xor(node.IpAddress.AsSpan()))
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
        if (!_peerCooldownCaching.TryGet(GetKey(peer.ClientId, peer.IpAddress), out _))
        {
            _peerCooldownCaching.AddOrUpdate(StoreDb.Key(peer.ClientId.ToString(), peer.IpAddress), peer);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void HandlePeerCooldown()
    {
        _coolDownDisposable = Observable.Interval(TimeSpan.FromMinutes(30)).Subscribe(_ =>
        {
            if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
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
            if (IPAddress.TryParse(value.FromBytes(), out var address))
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