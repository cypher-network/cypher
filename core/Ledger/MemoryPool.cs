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
using CypherNetwork.Persistence;
using Dawn;
using MessagePack;
using NBitcoin;
using Serilog;
using Transaction = CypherNetwork.Models.Transaction;

namespace CypherNetwork.Ledger;

/// <summary>
/// </summary>
public interface IMemoryPool
{
    Task<VerifyResult> NewTransactionAsync(Transaction transaction);
    Transaction Get(in byte[] hash);
    Transaction[] GetMany();
    Task<Transaction[]> GetVerifiedTransactionsAsync(int take);
    int Count();
}

/// <summary>
/// </summary>
public class MemoryPool : IMemoryPool, IDisposable
{
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;
    private readonly Caching<string> _syncCacheSeenTransactions = new();
    private readonly Caching<Transaction> _syncCacheTransactions = new();
    private IDisposable _disposableHandelSeenTransactions;
    private bool _disposed;

    /// <summary>
    /// </summary>
    /// <param name="cypherSystemCore"></param>
    /// <param name="logger"></param>
    public MemoryPool(ICypherSystemCore cypherSystemCore, ILogger logger)
    {
        _cypherSystemCore = cypherSystemCore;
        _logger = logger.ForContext("SourceContext", nameof(MemoryPool));
        Init();
    }

    /// <summary>
    /// </summary>
    /// <param name="transaction"></param>
    /// <returns></returns>
    public async Task<VerifyResult> NewTransactionAsync(Transaction transaction)
    {
        Guard.Argument(transaction, nameof(transaction)).NotNull();
        try
        {
            if (transaction.OutputType() == CoinType.Coinstake)
            {
                _logger.Fatal("Blocked coinstake transaction with {@TxId}", transaction.TxnId.ByteToHex());
                return VerifyResult.Invalid;
            }

            if (transaction.HasErrors().Any()) return VerifyResult.Invalid;
            if (!_syncCacheSeenTransactions.Contains(transaction.TxnId))
            {
                var broadcast = _cypherSystemCore.Broadcast();
                _syncCacheTransactions.Add(transaction.TxnId, transaction);
                _syncCacheSeenTransactions.Add(transaction.TxnId, transaction.TxnId.ByteToHex());
                await broadcast.PostAsync((TopicType.AddTransaction, MessagePackSerializer.Serialize(transaction)));
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
            return VerifyResult.Invalid;
        }

        return VerifyResult.Succeed;
    }

    /// <summary>
    /// </summary>
    /// <param name="transactionId"></param>
    /// <returns></returns>
    public Transaction Get(in byte[] transactionId)
    {
        Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);
        try
        {
            if (_syncCacheTransactions.TryGet(transactionId, out var transaction))
                return transaction;
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to find transaction with {@TxId}", transactionId.ByteToHex());
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public Transaction[] GetMany()
    {
        return _syncCacheTransactions.GetItems();
    }

    /// <summary>
    /// </summary>
    /// <param name="take"></param>
    /// <returns></returns>
    public async Task<Transaction[]> GetVerifiedTransactionsAsync(int take)
    {
        Guard.Argument(take, nameof(take)).NotNegative();
        var validTransactions = new List<Transaction>();
        var validator = _cypherSystemCore.Validator();
        foreach (var transaction in _syncCacheTransactions.GetItems().Take(take).Select(x => x)
                     .OrderByDescending(x => x.Vtime.I))
        {
            var verifyTransaction = await validator.VerifyTransactionAsync(transaction);
            if (verifyTransaction == VerifyResult.Succeed) validTransactions.Add(transaction);

            _syncCacheTransactions.Remove(transaction.TxnId);
        }

        return validTransactions.ToArray();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public int Count()
    {
        return _syncCacheTransactions.Count;
    }

    /// <summary>
    /// </summary>
    private void Init()
    {
        HandelSeenTransactions();
    }

    /// <summary>
    /// </summary>
    private void HandelSeenTransactions()
    {
        _disposableHandelSeenTransactions = Observable.Interval(TimeSpan.FromHours(1))
            .Subscribe(_ =>
            {
                if (_cypherSystemCore.ApplicationLifetime.ApplicationStopping.IsCancellationRequested) return;
                try
                {
                    var removeTransactionsBeforeTimestamp = Util.GetUtcNow().AddHours(-1).ToUnixTimestamp();
                    var syncCacheTransactions = _syncCacheTransactions.GetItems()
                        .Where(x => x.Vtime.L < removeTransactionsBeforeTimestamp);
                    foreach (var transaction in syncCacheTransactions)
                    {
                        _syncCacheTransactions.Remove(transaction.TxnId);
                        _syncCacheSeenTransactions.Remove(transaction.TxnId);
                    }
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
    /// <param name="disposing"></param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _disposableHandelSeenTransactions?.Dispose();
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