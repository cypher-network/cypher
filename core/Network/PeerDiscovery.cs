// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using CypherNetwork.Persistence;
using MessagePack;
using Microsoft.IO;
using Microsoft.Toolkit.HighPerformance;
using NBitcoin;
using Nerdbank.Streams;
using Serilog;

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

    /// <summary>
    /// 
    /// </summary>
    /// <param name="peer"></param>
    void SetPeerCooldown(PeerCooldown peer);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="publicKey"></param>
    /// <returns></returns>
    Task ReceivedPeersAsync(ReadOnlyMemory<byte> msg, ReadOnlyMemory<byte> publicKey);
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    ReadOnlySequence<byte> GetPeers();
}

/// <summary>
/// </summary>
public sealed class PeerDiscovery : IDisposable, IPeerDiscovery
{
    private readonly Caching<Peer> _caching = new();
    private readonly Caching<PeerCooldown> _peerCooldownCaching = new();
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;

    private LocalNode _localNode;
    private IDisposable _receiverDisposable;
    private IDisposable _coolDownDisposable;
    private Peer _localPeer;
    private RemoteNode[] _seedNodes;
    private bool _disposed;

    private static readonly object LockOnReady = new();
    private static readonly object LockOnBootstrap = new();

    /// <summary>
    /// </summary>
    /// <param name="cypherSystemCore"></param>
    /// <param name="logger"></param>
    public PeerDiscovery(ICypherSystemCore cypherSystemCore, ILogger logger)
    {
        _cypherSystemCore = cypherSystemCore;
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
    private void UpdateLocalPeerInfo()
    {
        _localPeer.BlockCount = _cypherSystemCore.UnitOfWork().HashChainRepository.Count;
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
    /// <returns></returns>
    public ReadOnlySequence<byte> GetPeers()
    {
        var sequence = new Sequence<byte>();
        UpdateLocalPeerInfo();
        IList<Peer> discoveryStore = _caching.GetItems().ToList();
        discoveryStore.Add(_localPeer);
        ReadOnlyPeerSequence(ref discoveryStore, ref sequence);
        return sequence.AsReadOnlySequence;
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
            Name = _localNode.Name,
            PublicKey = _localNode.PublicKey,
            Version = _localNode.Version
        };
        _seedNodes = new RemoteNode[_cypherSystemCore.Node.Network.SeedList.Count];
        foreach (var seedNode in _cypherSystemCore.Node.Network.SeedList.WithIndex())
        {
            var endpoint = Util.GetIpEndPoint(seedNode.item);
            _seedNodes[seedNode.index] = new RemoteNode(endpoint.Address.ToString().ToBytes(), endpoint.Port.ToBytes(),
                _cypherSystemCore.Node.Network.SeedListPublicKeys[seedNode.index].HexToByte());
        }

        ReceiverAsync().ConfigureAwait(false);
        HandlePeerCooldown();
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
        UpdateLocalPeerInfo();
        IList<Peer> discoveryStore = new List<Peer> { _localPeer };
        var parameter = new Parameter[]
        {
            new() { ProtocolCommand = ProtocolCommand.UpdatePeers, Value = MessagePackSerializer.Serialize(discoveryStore) }
        };
        var msg = MessagePackSerializer.Serialize(parameter);
        for (var index = 0; index < _seedNodes.Length; index++)
        {
            var i = index;
            tasks.Add(Task.Run(async () =>
            {
                var seedNode = _seedNodes[i];
                try
                {
                    var _ = await _cypherSystemCore.P2PDeviceReq().SendAsync<EmptyMessage>(seedNode.IpAddress,
                        seedNode.TcpPort, seedNode.PublicKey,
                        msg);
                }
                catch (Exception ex)
                {
                    _logger.Here().Error("{@Message}", ex.Message);
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
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
        IList<Peer> discoveryStore = _caching.GetItems().ToList();
        UpdateLocalPeerInfo();
        discoveryStore.Add(_localPeer);
        var parameter = new Parameter[]
        {
            new() { ProtocolCommand = ProtocolCommand.UpdatePeers, Value =  MessagePackSerializer.Serialize(discoveryStore) }
        };
        var msg = MessagePackSerializer.Serialize(parameter);
        for (var index = 0; index < discoveryStore.Count; index++)
        {
            var peer = discoveryStore[index];
            if (peer.ClientId == _localPeer.ClientId) continue;
            var storePeer = peer;
            try
            {
                if (await _cypherSystemCore.P2PDeviceReq().SendAsync<Ping>(storePeer.IpAddress, storePeer.TcpPort,
                        storePeer.PublicKey, msg) is not null)
                {
                    if (storePeer.Retries == 0) continue;
                    storePeer.Retries = 0;
                    UpdatePeer(storePeer.ClientId, storePeer.IpAddress, storePeer);
                }
                else
                {
                    if (storePeer.Retries >= 30)
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
                    else
                    {
                        storePeer.Retries++;
                        UpdatePeer(storePeer.ClientId, storePeer.IpAddress, storePeer);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error("{@Message}", ex.Message);
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="publicKey"></param>
    public async Task ReceivedPeersAsync(ReadOnlyMemory<byte> msg, ReadOnlyMemory<byte> publicKey)
    {
        await using var stream = Util.Manager.GetStream(msg.Span) as RecyclableMemoryStream;
        using var reader = new MessagePackStreamReader(stream);
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
            var key = GetKey(peer.ClientId, peer.IpAddress);
            if (!_caching.TryGet(key, out var cachedPeer))
            {
                if (_peerCooldownCaching[key].IsDefault())
                {
                    UpdatePeer(peer.ClientId, peer.IpAddress, peer);
                }
                else if (peer.PublicKey[1..33].Xor(publicKey.ToArray()))
                {
                    _peerCooldownCaching.Remove(key);
                    UpdatePeer(peer.ClientId, peer.IpAddress, peer);
                }
            }
            else if (cachedPeer.BlockCount != peer.BlockCount)
            {
                peer.Retries = cachedPeer.Retries;
                UpdatePeer(peer.ClientId, peer.IpAddress, peer);
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
        _coolDownDisposable = Observable.Interval(TimeSpan.FromMinutes(10)).Subscribe(_ =>
        {
            if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
            try
            {

                var removePeersCooldown = AsyncHelper.RunSync(async delegate
                {
                    var removePeerCooldownBeforeTimestamp = Util.GetUtcNow().AddMinutes(-10).ToUnixTimestamp();
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
            _receiverDisposable?.Dispose();
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