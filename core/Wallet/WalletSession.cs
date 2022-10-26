// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Security;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using CypherNetwork.Persistence;
using CypherNetwork.Wallet.Models;
using Dawn;
using MessagePack;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using Serilog;
using Block = CypherNetwork.Models.Block;
using Transaction = CypherNetwork.Models.Transaction;

namespace CypherNetwork.Wallet;

/// <summary>
/// 
/// </summary>
public record Consumed(byte[] Commit, DateTime Time)
{
    public readonly DateTime Time = Time;
    public readonly byte[] Commit = Commit;
}

/// <summary>
/// </summary>
public class WalletSession : IWalletSession, IDisposable
{
    private const string HardwarePath = "m/44'/847177'/0'/0/";

    public Caching<Output> CacheTransactions { get; } = new();
    public Cache<Consumed> CacheConsumed { get; } = new();
    public Output Spending { get; set; }
    public SecureString Seed { get; set; }
    public SecureString Passphrase { get; set; }
    public string SenderAddress { get; set; }
    public string RecipientAddress { get; set; }
    public SecureString KeySet { get; set; }
    public ulong Amount { get; set; }
    public ulong Change { get; set; }
    public ulong Reward { get; set; }

    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger _logger;
    private readonly NBitcoin.Network _network;

    private IDisposable _disposableHandleSafeguardBlocks;
    private IDisposable _disposableHandelConsumed;
    private bool _disposed;
    private IReadOnlyList<Block> _readOnlySafeGuardBlocks;

    private static readonly object Locking = new();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cypherSystemCore"></param>
    /// <param name="applicationLifetime"></param>
    /// <param name="logger"></param>
    public WalletSession(ICypherSystemCore cypherSystemCore, IHostApplicationLifetime applicationLifetime, ILogger logger)
    {
        _cypherSystemCore = cypherSystemCore;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _network = cypherSystemCore.Node.Network.Environment == Node.Mainnet
            ? NBitcoin.Network.Main
            : NBitcoin.Network.TestNet;
        Init();
    }

    /// <summary>
    /// 
    /// </summary>
    private void Init()
    {
        HandleSafeguardBlocks();
        HandelConsumed();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="transactions"></param>
    public void Notify(Transaction[] transactions)
    {
        if (KeySet is null) return;
        foreach (var consumed in CacheConsumed.GetItems())
        {
            var transaction = transactions.FirstOrDefault(t => t.Vout.Any(c => c.C.Xor(consumed.Commit)));
            if (transaction.IsDefault()) continue;
            CacheConsumed.Remove(consumed.Commit);
            CacheTransactions.Remove(consumed.Commit);
            break;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="seed"></param>
    /// <param name="passphrase"></param>
    /// <returns></returns>
    public Task<Tuple<bool, string>> LoginAsync(byte[] seed)
    {
        Guard.Argument(seed, nameof(seed)).NotNull().NotEmpty();
        try
        {
            Seed = seed.ToSecureString();
            seed.Destroy();
            CreateHdRootKey(Seed, out var rootKey);
            var keySet = CreateKeySet(new KeyPath($"{HardwarePath}0"), rootKey.PrivateKey.ToHex().HexToByte(),
                rootKey.ChainCode);
            SenderAddress = keySet.StealthAddress;
            KeySet = MessagePackSerializer.Serialize(keySet).ByteToHex().ToSecureString();
            keySet.ChainCode.ZeroString();
            keySet.KeyPath.ZeroString();
            keySet.RootKey.ZeroString();
            return Task.FromResult(new Tuple<bool, string>(true, "Wallet login successful"));
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return Task.FromResult(new Tuple<bool, string>(false, "Unable to login"));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="outputs"></param>
    /// <returns></returns>
    public Task<Tuple<bool, string>> InitializeWalletAsync(Output[] outputs)
    {
        Guard.Argument(outputs, nameof(outputs)).NotNull().NotEmpty();
        try
        {
            if (KeySet is null) return Task.FromResult(new Tuple<bool, string>(false, "Node wallet login required"));
            CacheTransactions.Clear();
            foreach (var vout in outputs) CacheTransactions.Add(vout.C, vout);
            const string pPoSMessageEnabled = "Pure Proof of Stake [ENABLED]";
            _logger.Information(pPoSMessageEnabled);
            return Task.FromResult(new Tuple<bool, string>(true,
                $"Node wallet received transactions. {pPoSMessageEnabled}"));
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return Task.FromResult(new Tuple<bool, string>(false, "Node wallet setup failed"));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<Block> GetSafeGuardBlocks()
    {
        lock (Locking)
        {
            return _readOnlySafeGuardBlocks;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="keyPath"></param>
    /// <param name="secretKey"></param>
    /// <param name="chainCode"></param>
    /// <returns></returns>
    private KeySet CreateKeySet(KeyPath keyPath, byte[] secretKey, byte[] chainCode)
    {
        Guard.Argument(keyPath, nameof(keyPath)).NotNull();
        Guard.Argument(secretKey, nameof(secretKey)).NotNull().MaxCount(32);
        Guard.Argument(chainCode, nameof(chainCode)).NotNull().MaxCount(32);
        var masterKey = new ExtKey(new Key(secretKey), chainCode);
        var spendKey = masterKey.Derive(keyPath).PrivateKey;
        var scanKey = masterKey.Derive(keyPath = keyPath.Increment()).PrivateKey;
        return new KeySet
        {
            ChainCode = masterKey.ChainCode.ByteToHex(),
            KeyPath = keyPath.ToString(),
            RootKey = masterKey.PrivateKey.ToHex(),
            StealthAddress = spendKey.PubKey.CreateStealthAddress(scanKey.PubKey, _network).ToString()
        };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="seed"></param>
    /// <param name="hdRoot"></param>
    private static void CreateHdRootKey(SecureString seed, out ExtKey hdRoot)
    {
        Guard.Argument(seed, nameof(seed)).NotNull();
        Guard.Argument(seed, nameof(seed)).NotNull();
        var concatenateMnemonic = string.Join(" ", seed.FromSecureString());
        hdRoot = new Mnemonic(concatenateMnemonic).DeriveExtKey();
        concatenateMnemonic.ZeroString();
    }

    /// <summary>
    /// 
    /// </summary>
    private void HandelConsumed()
    {
        _disposableHandelConsumed = Observable.Interval(TimeSpan.FromMilliseconds(10000))
            .Subscribe(_ =>
            {
                if (_applicationLifetime.ApplicationStopping.IsCancellationRequested) return;
                try
                {
                    var removeUnused = Helper.Util.GetUtcNow().AddSeconds(-30);
                    foreach (var consumed in CacheConsumed.GetItems())
                    {
                        if (consumed.Time < removeUnused)
                        {
                            CacheConsumed.Remove(consumed.Commit);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("{@}", ex.Message);
                }
            });
    }

    /// <summary>
    /// 
    /// </summary>
    private void HandleSafeguardBlocks()
    {
        _disposableHandleSafeguardBlocks = Observable.Timer(TimeSpan.Zero, TimeSpan.FromMilliseconds(155520000))
            .Select(_ => Observable.FromAsync(async () =>
            {
                try
                {
                    if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
                    var blocksResponse = await _cypherSystemCore.Graph().GetSafeguardBlocksAsync(new SafeguardBlocksRequest(147));
                    lock (Locking)
                    {
                        _readOnlySafeGuardBlocks = blocksResponse.Blocks;
                    }
                }
                catch (Exception)
                {
                    // Ignore
                }
            }))
            .Merge()
            .Subscribe();
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
            _disposableHandelConsumed?.Dispose();
            _disposableHandleSafeguardBlocks?.Dispose();
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