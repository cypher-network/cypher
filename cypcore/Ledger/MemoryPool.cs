// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using Dawn;
using MessagePack;
using NBitcoin;
using Proto;
using Proto.DependencyInjection;
using Serilog;
using Transaction = CYPCore.Models.Transaction;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IMemoryPool
    {
        Task<VerifyResult> NewTransaction(Transaction transaction);
        Transaction Get(in byte[] hash);
        Task<Transaction[]> GetMany();
        Task<Transaction[]> GetVerifiedTransactions(int skip, int take);
        VerifyResult Delete(in Transaction transaction);
        int Count();
    }

    /// <summary>
    /// 
    /// </summary>
    public class MemoryPool : IMemoryPool
    {
        public const uint TransactionDefaultTimeDelayFromSeconds = 5;

        private readonly ActorSystem _actorSystem;
        private readonly PID _pidLocalNode;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly MemStore<Transaction> _memStoreTransactions = new();
        private readonly MemStore<string> _memStoreSeenTransactions = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="validator"></param>
        /// <param name="logger"></param>
        public MemoryPool(ActorSystem actorSystem, IValidator validator, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidLocalNode = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<LocalNode>());
            _validator = validator;
            _logger = logger.ForContext("SourceContext", nameof(MemoryPool));
            Observable.Timer(TimeSpan.Zero, TimeSpan.FromHours(1)).Subscribe(_ =>
            {
                var removeTransactionsBeforeTimestamp = Util.GetUtcNow().AddHours(-1).ToUnixTimestamp();
                var snapshot = _memStoreTransactions.GetMemSnapshot().SnapshotAsync().ToEnumerable();
                var removeTransactions = snapshot.Where(x => x.Value.Vtime.L < removeTransactionsBeforeTimestamp);
                foreach (var (key, _) in removeTransactions)
                {
                    _memStoreTransactions.Delete(key);
                    _memStoreSeenTransactions.Delete(key);
                }
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public Task<VerifyResult> NewTransaction(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            try
            {
                var outputs = transaction.Vout.Select(x => x.T.ToString()).ToArray();
                if (outputs.Contains(CoinType.Coinbase.ToString()) && outputs.Contains(CoinType.Coinstake.ToString()))
                {
                    _logger.Here().Fatal("Blocked coinstake transaction with {@txnId}", transaction.TxnId.ByteToHex());
                    return Task.FromResult(VerifyResult.Invalid);
                }

                if (transaction.Validate().Any()) return Task.FromResult(VerifyResult.Invalid);
                if (!_memStoreSeenTransactions.Contains(transaction.TxnId))
                {
                    _memStoreTransactions.Put(transaction.TxnId, transaction);
                    _memStoreSeenTransactions.Put(transaction.TxnId, transaction.TxnId.ByteToHex());
                    _actorSystem.Root.Send(_pidLocalNode,
                        new BroadcastAutoRequest(TopicType.AddTransaction,
                            MessagePackSerializer.Serialize(transaction)));
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, ex.Message);
                return Task.FromResult(VerifyResult.Invalid);
            }

            return Task.FromResult(VerifyResult.Succeed);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionId"></param>
        /// <returns></returns>
        public Transaction Get(in byte[] transactionId)
        {
            Guard.Argument(transactionId, nameof(transactionId)).NotNull().MaxCount(32);
            Transaction transaction = null;
            try
            {
                _memStoreTransactions.TryGet(transactionId, out transaction);
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
        public async Task<Transaction[]> GetMany()
        {
            return await _memStoreTransactions.GetMemSnapshot().SnapshotAsync().Select(x => x.Value).ToArrayAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<Transaction[]> GetVerifiedTransactions(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();
            var validTransactions = new List<Transaction>();
            await foreach (var transaction in _memStoreTransactions.GetMemSnapshot().SnapshotAsync()
                .Select(x => x.Value).OrderByDescending(x => x.Vtime.I))
            {
                var verifyTransaction = await _validator.VerifyTransaction(transaction);
                if (verifyTransaction == VerifyResult.Succeed)
                {
                    validTransactions.Add(transaction);
                }

                _memStoreTransactions.Delete(transaction.TxnId);
            }

            return validTransactions.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult Delete(in Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            try
            {
                _memStoreTransactions.Delete(transaction.TxnId);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to remove transaction with {@TxnId}", transaction.TxnId);
            }

            return _memStoreTransactions.Contains(transaction.TxnId) ? VerifyResult.Succeed : VerifyResult.Invalid;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            return _memStoreTransactions.Count();
        }
    }
}