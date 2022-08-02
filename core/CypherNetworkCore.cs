// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Security;
using System.Threading.Tasks;
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
public interface ICypherNetworkCore
{
    IHostApplicationLifetime ApplicationLifetime { get; }
    IServiceScopeFactory ServiceScopeFactory { get; }
    AppOptions AppOptions { get; }
    KeyPair KeyPair { get; }
    AsyncLazy<IUnitOfWork> UnitOfWork();
    AsyncLazy<IPeerDiscovery> PeerDiscovery();
    AsyncLazy<IGraph> Graph();
    AsyncLazy<IPPoS> PPoS();
    IValidator Validator();
    AsyncLazy<ISync> Sync();
    AsyncLazy<IMemoryPool> MemPool();
    INodeWallet Wallet();
    AsyncLazy<IWalletSession> WalletSession();
    IBroadcast Broadcast();
    ICrypto Crypto();
    IP2PDevice P2PDevice();
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
public class CypherNetworkCore : ICypherNetworkCore
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

    /// <summary>
    /// </summary>
    /// <param name="applicationLifetime"></param>
    /// <param name="serviceScopeFactory"></param>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    public CypherNetworkCore(IHostApplicationLifetime applicationLifetime,
        IServiceScopeFactory serviceScopeFactory, AppOptions options, ILogger logger)
    {
        ApplicationLifetime = applicationLifetime;
        ServiceScopeFactory = serviceScopeFactory;
        AppOptions = options;
        _logger = logger;
        await InitAsync();
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
    public AppOptions AppOptions { get; }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public AsyncLazy<IUnitOfWork> UnitOfWork() => new(() =>
    {
        _unitOfWork ??= GetUnitOfWork();
        return Task.FromResult(_unitOfWork);
    });

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public AsyncLazy<IPeerDiscovery> PeerDiscovery() => new(() =>
    {
        _peerDiscovery ??= GetPeerDiscovery();
        return Task.FromResult(_peerDiscovery);
    });

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public AsyncLazy<IGraph> Graph() => new(() =>
    {
        _graph ??= GetGraph();
        return Task.FromResult(_graph);
    });

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public AsyncLazy<IPPoS> PPoS() => new(() =>
    {
        _poS ??= GetPPoS();
        return Task.FromResult(_poS);
    });

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
    public AsyncLazy<ISync> Sync() => new(() =>
    {
        _sync ??= GetSync();
        return Task.FromResult(_sync);
    });

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public AsyncLazy<IMemoryPool> MemPool() => new(() =>
    {
        _memoryPool ??= GetMemPool();
        return Task.FromResult(_memoryPool);
    });

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
    public AsyncLazy<IWalletSession> WalletSession() => new(() =>
    {
        _walletSession ??= GetWalletSession();
        return Task.FromResult(_walletSession);
    });

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
    public ICrypto Crypto()
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
    public IP2PDevice P2PDevice()
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
    public Cache<object> Cache()
    {
        return _cache;
    }

    /// <summary>
    /// </summary>
    private async Task InitAsync()
    {
        var crypto = Crypto();
        var keyPair = await AsyncHelper.RunSyncAsync(() => crypto.GetOrUpsertKeyNameAsync(AppOptions.Network.SigningKeyRingName));
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