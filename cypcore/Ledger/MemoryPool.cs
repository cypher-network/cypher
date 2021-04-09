// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Collections.Pooled;
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
        public VerifyResult Add(byte[] transactionModel);
        TransactionModel Get(byte[] hash);
        TransactionModel[] GetMany();
        TransactionModel[] Range(int skip, int take);
        IObservable<TransactionModel> ObserveRange(int skip, int take);
        IObservable<TransactionModel> ObserveTake(int take);
        VerifyResult Remove(TransactionModel transaction);
        int Count();
    }

    /// <summary>
    /// 
    /// </summary>
    public class MemoryPool : IMemoryPool
    {
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;
        private readonly PooledList<TransactionModel> _pooledList;

        private const int MemoryPoolMaxTransactions = 10_000;

        public MemoryPool(ILocalNode localNode, ILogger logger)
        {
            _localNode = localNode;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPool));
            _pooledList = new PooledList<TransactionModel>(MemoryPoolMaxTransactions);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionModel"></param>
        /// <returns></returns>
        public VerifyResult Add(byte[] transactionModel)
        {
            Guard.Argument(transactionModel, nameof(transactionModel)).NotNull();

            try
            {
                var transaction = Helper.Util.DeserializeFlatBuffer<TransactionModel>(transactionModel);
                if (transaction.Validate().Any()) return VerifyResult.Invalid;

                if (!_pooledList.Contains(transaction))
                {
                    _pooledList.Add(transaction);
                }

                _localNode.Broadcast(TopicType.AddTransaction, transactionModel);
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
        public TransactionModel Get(byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);

            TransactionModel transaction = null;

            try
            {
                transaction = _pooledList.FirstOrDefault(x => x.TxnId == transactionId.HexToByte());
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to find transaction with {@txnId}", transactionId.ByteToHex());
            }

            return transaction;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public TransactionModel[] GetMany()
        {
            return _pooledList.Select(x => x).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public TransactionModel[] Range(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            return _pooledList.Skip(skip).Take(take).Select(x => x).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public IObservable<TransactionModel> ObserveRange(int skip, int take)
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
        public IObservable<TransactionModel> ObserveTake(int take)
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
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult Remove(TransactionModel transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var removed = false;

            try
            {
                removed = _pooledList.Remove(transaction);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to remove transaction with {@TxnId}", transaction.TxnId);
            }

            return removed ? VerifyResult.Succeed : VerifyResult.Invalid;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            return _pooledList.Count;
        }
    }
}