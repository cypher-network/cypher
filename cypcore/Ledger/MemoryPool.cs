// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Collections.Pooled;
using Dawn;
using Serilog;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Network;
using MessagePack;
using NBitcoin;
using Transaction = CYPCore.Models.Transaction;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IMemoryPool
    {
        public Task<VerifyResult> Add(byte[] transactionModel);
        Transaction Get(byte[] hash);
        Transaction[] GetMany();
        Transaction[] Range(int skip, int take);
        IObservable<Transaction> ObserveRange(int skip, int take);
        IObservable<Transaction> ObserveTake(int take);
        VerifyResult Remove(Transaction transaction);
        int Count();
    }

    /// <summary>
    /// 
    /// </summary>
    public class MemoryPool : IMemoryPool
    {
        public const uint TransactionTimeSlot = 0x00000005;

        private readonly ILocalNode _localNode;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly PooledList<Transaction> _pooledTransactions;
        private readonly PooledList<string> _pooledSeenTransactions;
        private readonly LeakyBucket _leakyBucket;
        private const int MaxMemoryPoolTransactions = 10_000;
        private const int MaxMemoryPoolSeenTransactions = 50_000;

        public MemoryPool(ILocalNode localNode, IValidator validator,
            ILeakRateConfigurationOption leakRateConfigurationOption, ILogger logger)
        {
            _localNode = localNode;
            _validator = validator;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPool));
            _pooledTransactions = new PooledList<Transaction>(MaxMemoryPoolTransactions);
            _pooledSeenTransactions = new PooledList<string>(MaxMemoryPoolSeenTransactions);
            _leakyBucket = new LeakyBucket(new BucketConfiguration
            {
                LeakRate = leakRateConfigurationOption.LeakRate,
                LeakRateTimeSpan = TimeSpan.FromSeconds(leakRateConfigurationOption.LeakRateNumberOfSeconds),
                MaxFill = leakRateConfigurationOption.MaxFill
            });

            Observable.Timer(TimeSpan.Zero, TimeSpan.FromHours(1)).Subscribe(x =>
            {
                _pooledSeenTransactions.RemoveRange(0, Count());
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionModel"></param>
        /// <returns></returns>
        public async Task<VerifyResult> Add(byte[] transactionModel)
        {
            Guard.Argument(transactionModel, nameof(transactionModel)).NotNull();
            try
            {
                await _leakyBucket.Wait();

                var transaction = MessagePackSerializer.Deserialize<Transaction>(transactionModel);
                if (transaction.Validate().Any()) return VerifyResult.Invalid;
                if (!_pooledSeenTransactions.Contains(transaction.TxnId.ByteToHex()))
                {
                    _pooledSeenTransactions.Add(transaction.TxnId.ByteToHex());
                    _pooledTransactions.Add(transaction);
                    await _localNode.Broadcast(TopicType.AddTransaction, transactionModel);
                }
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
        public Transaction Get(byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);
            Transaction transaction = null;
            try
            {
                transaction = _pooledTransactions.FirstOrDefault(x => x.TxnId == transactionId.HexToByte());
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
        public Transaction[] GetMany()
        {
            return _pooledTransactions.Select(x => x).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public Transaction[] Range(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            return _pooledTransactions.Skip(skip).Take(take).Select(x => x).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public IObservable<Transaction> ObserveRange(int skip, int take)
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
        public IObservable<Transaction> ObserveTake(int take)
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
        public VerifyResult Remove(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            var removed = false;
            try
            {
                removed = _pooledTransactions.Remove(transaction);
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
            return _pooledTransactions.Count;
        }
    }
}