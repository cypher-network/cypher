// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Security;
using CypherNetwork.Cryptography;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Ledger;
using CypherNetwork.Models;
using CypherNetwork.Network;
using CypherNetwork.Persistence;
using CypherNetwork.Wallet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CypherNetwork;

/// <summary>
/// </summary>
public interface ICypherSystemCore
{
    IHostApplicationLifetime ApplicationLifetime { get; }
    IServiceScopeFactory ServiceScopeFactory { get; }
    Node Node { get; }
    KeyPair KeyPair { get; }
    IUnitOfWork UnitOfWork();
    IPeerDiscovery PeerDiscovery();
    IGraph Graph();
    IPPoS PPoS();
    IValidator Validator();
    ISync Sync();
    IMemoryPool MemPool();
    INodeWallet Wallet();
    IWalletSession WalletSession();
    IBroadcast Broadcast();
    ICrypto Crypto();
    IP2PDevice P2PDevice();
    IP2PDeviceApi P2PDeviceApi();
    IP2PDeviceReq P2PDeviceReq();
    Cache<object> Cache();
}

/// <summary>
/// </summary>
public record KeyPair
{
    public SecureString PrivateKey { get; init; }

    /// <summary>
    /// </summary>
    public byte[] PublicKey { get; init; }
}

public class Cache<T> : Caching<T> where T : class { }

/// <summary>
/// </summary>
public class CypherSystemCore : ICypherSystemCore
{
    private readonly ILogger _logger;
    private readonly Cache<object> _cache = new();

    private IUnitOfWork _unitOfWork;
    private IPeerDiscovery _peerDiscovery;
    private IGraph _graph;
    private IPPoS _poS;
    private ISync _sync;
    private IMemoryPool _memoryPool;
    private IWalletSession _walletSession;
    private IP2PDevice _p2PDevice;
    private IP2PDeviceApi _p2PDeviceApi;
    private IP2PDeviceReq _p2PDeviceReq;
    private ICrypto _crypto;
    
    /// <summary>
    /// </summary>
    /// <param name="applicationLifetime"></param>
    /// <param name="serviceScopeFactory"></param>
    /// <param name="node"></param>
    /// <param name="logger"></param>
    public CypherSystemCore(IHostApplicationLifetime applicationLifetime,
        IServiceScopeFactory serviceScopeFactory, Node node, ILogger logger)
    {
        ApplicationLifetime = applicationLifetime;
        ServiceScopeFactory = serviceScopeFactory;
        Node = node;
        _logger = logger;
        Init();
    }

    /// <summary>
    /// </summary>
    public KeyPair KeyPair { get; private set; }

    /// <summary>
    /// </summary>
    public IHostApplicationLifetime ApplicationLifetime { get; }

    /// <summary>
    /// </summary>
    public IServiceScopeFactory ServiceScopeFactory { get; }

    /// <summary>
    /// </summary>
    public Node Node { get; }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IUnitOfWork UnitOfWork()
    {
        _unitOfWork ??= GetUnitOfWork();
        return _unitOfWork;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IPeerDiscovery PeerDiscovery()
    {
        _peerDiscovery ??= GetPeerDiscovery();
        return _peerDiscovery;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IGraph Graph()
    {
        _graph ??= GetGraph();
        return _graph;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public IPPoS PPoS()
    {
        _poS ??= GetPPoS();
        return _poS;
    }

    public ICrypto Crypto()
    {
        _crypto ??= GetCrypto();
        return _crypto;
    }
    
    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IValidator Validator()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var validator = scope.ServiceProvider.GetRequiredService<IValidator>();
            return validator;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public ISync Sync()
    {
        _sync ??= GetSync();
        return _sync;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IMemoryPool MemPool()
    {
        _memoryPool ??= GetMemPool();
        return _memoryPool;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public IP2PDevice P2PDevice()
    {
        _p2PDevice ??= GetP2PDevice();
        return _p2PDevice;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public IP2PDeviceApi P2PDeviceApi()
    {
        _p2PDeviceApi ??= GetP2PDeviceApi();
        return _p2PDeviceApi;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public IP2PDeviceReq P2PDeviceReq()
    {
        _p2PDeviceReq ??= GetP2PDeviceReq();
        return _p2PDeviceReq;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public INodeWallet Wallet()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var wallet = scope.ServiceProvider.GetRequiredService<INodeWallet>();
            return wallet;
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
    /// <returns></returns>
    public IWalletSession WalletSession()
    {
        _walletSession ??= GetWalletSession();
        return _walletSession;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IBroadcast Broadcast()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var broadcast = scope.ServiceProvider.GetRequiredService<IBroadcast>();
            return broadcast;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private ICrypto GetCrypto()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var crypto = scope.ServiceProvider.GetRequiredService<ICrypto>();
            return crypto;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private IP2PDevice GetP2PDevice()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var p2PDevice = scope.ServiceProvider.GetRequiredService<IP2PDevice>();
            return p2PDevice;
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
    /// <returns></returns>
    private IP2PDeviceReq GetP2PDeviceReq()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var p2PDeviceReq = scope.ServiceProvider.GetRequiredService<IP2PDeviceReq>();
            return p2PDeviceReq;
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
    /// <returns></returns>
    private IP2PDeviceApi GetP2PDeviceApi()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var p2PDeviceApi = scope.ServiceProvider.GetRequiredService<IP2PDeviceApi>();
            return p2PDeviceApi;
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
    /// <returns></returns>
    public Cache<object> Cache()
    {
        return _cache;
    }

    /// <summary>
    /// </summary>
    private void Init()
    {
        _crypto = GetCrypto();
        var keyPair = AsyncHelper.RunSync(() => _crypto.GetOrUpsertKeyNameAsync(Node.Network.SigningKeyRingName));
        KeyPair = new KeyPair
        {
            PrivateKey = keyPair.PrivateKey.ByteToHex().ToSecureString(),
            PublicKey = keyPair.PublicKey
        };
        keyPair.PrivateKey.Destroy();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private IUnitOfWork GetUnitOfWork()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            return unitOfWork;
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
    /// <returns></returns>
    private IPeerDiscovery GetPeerDiscovery()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateScope();
            var peerDiscovery = scope.ServiceProvider.GetRequiredService<IPeerDiscovery>();
            return peerDiscovery;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private IPPoS GetPPoS()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var pPoS = scope.ServiceProvider.GetRequiredService<IPPoS>();
            return pPoS;
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
    /// <returns></returns>
    private IGraph GetGraph()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateScope();
            var graph = scope.ServiceProvider.GetRequiredService<IGraph>();
            return graph;
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
    /// <returns></returns>
    private ISync GetSync()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<ISync>();
            return sync;
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
    /// <returns></returns>
    private IMemoryPool GetMemPool()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var memoryPool = scope.ServiceProvider.GetRequiredService<IMemoryPool>();
            return memoryPool;
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
    /// <returns></returns>
    private IWalletSession GetWalletSession()
    {
        try
        {
            using var scope = ServiceScopeFactory.CreateAsyncScope();
            var walletSession = scope.ServiceProvider.GetRequiredService<IWalletSession>();
            return walletSession;
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return null;
    }
}