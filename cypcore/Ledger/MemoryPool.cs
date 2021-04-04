// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using Dawn;
using Serilog;
using CYPCore.Extentions;
using CYPCore.Models;
using CYPCore.Extensions;
using CYPCore.Network;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IMemoryPool
    {
        VerifyResult Add(TransactionProto tx);
        TransactionProto Get(byte[] hash);
        TransactionProto[] GetMany();
        TransactionProto[] Range(int skip, int take);
        IObservable<TransactionProto> ObserveRange(int skip, int take);
        IObservable<TransactionProto> ObserveTake(int take);
        VerifyResult Remove(byte[] hash);
        int Count();
    }

    /// <summary>
    /// 
    /// </summary>
    public class MemoryPool : IMemoryPool
    {
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, Lazy<TransactionProto>> _concurrentTransactions;
        private const int MemoryPoolMaxTransactions = 10_000;

        public MemoryPool(ILocalNode localNode, ILogger logger)
        {
            _localNode = localNode;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPool));
            _concurrentTransactions = new ConcurrentDictionary<string, Lazy<TransactionProto>>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult Add(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            try
            {
                if (transaction.Validate().Any()) return VerifyResult.Invalid;

                var memoryMax = Count() > MemoryPoolMaxTransactions;
                if (memoryMax) return VerifyResult.OutOfMemory;

                var adding = GetOrAddTransaction(transaction.TxnId.ByteToHex(), s => transaction);
                if (adding != null)
                {
                    return VerifyResult.AlreadyExists;
                }

                var buffer = Helper.Util.SerializeFlatBuffer(transaction);
                _localNode.Broadcast(buffer, TopicType.AddTransaction);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, ex.Message);
                return VerifyResult.Invalid;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionId"></param>
        /// <returns></returns>
        public TransactionProto Get(byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);

            TransactionProto tx = null;

            try
            {
                tx = GetOrAddTransaction(transactionId.ByteToHex(), s => null);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to find transaction with {@txnId}", transactionId.ByteToHex());
            }

            return tx;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public TransactionProto[] GetMany()
        {
            return _concurrentTransactions.Select(x => x.Value.Value).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public TransactionProto[] Range(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            return _concurrentTransactions.Skip(skip).Take(take).Select(x => x.Value.Value).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public IObservable<TransactionProto> ObserveRange(int skip, int take)
        {
            return Observable.Defer(() =>
            {
                var transactions = Range(skip, take);
                return transactions.ToObservable();
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="take"></param>
        /// <returns></returns>
        public IObservable<TransactionProto> ObserveTake(int take)
        {
            return Observable.Defer(() =>
            {
                var transactions = Range(0, take);
                return transactions.ToObservable();
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionId"></param>
        /// <returns></returns>
        public VerifyResult Remove(byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);
            var removed = false;

            try
            {
                removed = _concurrentTransactions.Remove(transactionId.ByteToHex(), out var transaction);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to remove transaction with {@txnId}", transactionId.ByteToHex());
            }

            return removed ? VerifyResult.Succeed : VerifyResult.Invalid;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            return _concurrentTransactions.Count;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="valueFactory"></param>
        /// <returns></returns>
        private TransactionProto GetOrAddTransaction(string key, Func<string, TransactionProto> valueFactory)
        {
            Guard.Argument(key, nameof(key)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(valueFactory, nameof(valueFactory)).NotNull();

            var lazyResult = _concurrentTransactions.GetOrAdd(key,
                k => new Lazy<TransactionProto>(() => valueFactory(k), LazyThreadSafetyMode.ExecutionAndPublication));
            return lazyResult.Value;
        }
    }
}