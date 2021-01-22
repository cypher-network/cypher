// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Models;
using CYPCore.Serf;
using CYPCore.Persistence;
using CYPCore.Extentions;
using CYPCore.Cryptography;
using CYPCore.Ledger;

namespace CYPNode.Services
{
    public class TransactionService : ITransactionService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMempool _mempool;
        private readonly ILogger _logger;
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signingProvider;

        /// <summary>
        ///
        /// </summary>
        /// <param name="unitOfWork"></param>
        /// <param name="mempool"></param>
        /// <param name="serfClient"></param>
        /// <param name="signingProvider"></param>
        /// <param name="logger"></param>
        public TransactionService(IUnitOfWork unitOfWork, IMempool mempool,
            ISerfClient serfClient, ISigning signingProvider, ILogger<TransactionService> logger)
        {
            _unitOfWork = unitOfWork;
            _mempool = mempool;
            _serfClient = serfClient;
            _signingProvider = signingProvider;
            _logger = logger;
        }

        /// <summary>
        /// Add transaction
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task<byte[]> AddTransaction(TransactionProto tx)
        {
            Guard.Argument(tx, nameof(tx)).NotNull();

            try
            {
                bool valid = tx.Validate().Any();
                if (!valid)
                {
                    bool delivered = await DeliveredTxExist(tx);
                    if (delivered)
                    {
                        return await Payload(tx, "K_Image exists.", true);
                    }

                    bool mempool = await MempoolTxExist(tx);
                    if (mempool)
                    {
                        return await Payload(tx, "Exists in mempool.", true);
                    }

                    var memPoolProto = await _mempool.AddMemPoolTransaction(MempoolProtoFactory(tx));
                    if (memPoolProto == null)
                    {
                        return await Payload(tx, "Unable to add txn mempool.", true);
                    }

                    return await Payload(memPoolProto.Block.Transaction, string.Empty, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< TransactionService.AddTransaction >>>: {ex}");
            }

            return await Payload(tx, "No message", true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="txnId"></param>
        /// <returns></returns>
        public async Task<byte[]> GetTransaction(byte[] txnId)
        {
            Guard.Argument(txnId, nameof(txnId)).NotNull().MaxCount(48);

            byte[] transaction = null;

            try
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x => new ValueTask<bool>(x.Transactions.Any(t => t.TxnId.SequenceEqual(txnId))));
                if (blockHeaders.Any())
                {
                    var found = blockHeaders.First().Transactions.FirstOrDefault(x => x.TxnId.SequenceEqual(txnId));
                    if (found != null)
                    {
                        transaction = CYPCore.Helper.Util.SerializeProto(found.Vout);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< TransactionService.GetTransaction >>>: {ex}");
            }

            return transaction;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<byte[]> GetSafeguardTransactions()
        {
            byte[] result = null;

            try
            {
                int count = await _unitOfWork.DeliveredRepository.CountAsync();
                var last = await _unitOfWork.DeliveredRepository.LastOrDefaultAsync();

                if (last != null)
                {
                    int height = (int)last.Height - count;

                    height = height > 0 ? 0 : height;

                    var blockHeaders = await _unitOfWork.DeliveredRepository.RangeAsync(height, 147);

                    if (blockHeaders?.Any() == true)
                    {
                        result = CYPCore.Helper.Util.SerializeProto(blockHeaders);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< TransactionService.GetSafeguardTransactions >>>: {ex}");
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<byte[]> GetTransactions(string key, int skip, int take)
        {
            Guard.Argument(key, nameof(key)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            byte[] result = null;

            try
            {
                result = await GetTransactions(key);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< TransactionService.GetTransactions >>>: {ex}");
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<byte[]> GetTransactions(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            byte[] result = null;

            try
            {
                var blockIds = await _unitOfWork.InterpretedRepository.RangeAsync(skip, take);
                if (blockIds?.Any() == true)
                {
                    result = CYPCore.Helper.Util.SerializeProto(blockIds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< TransactionService.GetTransactions >>>: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Gets transactions.
        /// TODO: Check deletion. Function body is commented out.
        /// </summary>
        /// <returns>List of transactions.</returns>
        /// <param name="key">Key.</param>
        public async Task<byte[]> GetTransactions(string key)
        {
            //if (string.IsNullOrEmpty(key))
            //    throw new ArgumentNullException(nameof(key));

            //byte[] result = null;

            //try
            //{
            //    var blockIds = await _unitOfWork.InterpretedRepository.WhereAsync(x => new ValueTask<bool>(x.SignedBlock.Transaction.PreImage.Equals(key)));
            //    if (blockIds?.Any() == true)
            //    {
            //        result = TGMCore.Helper.Util.SerializeProto(blockIds);
            //    }
            //}
            //catch (Exception ex)
            //{
            //    _logger.LogError($"<<< TransactionService.GetTransactions >>>: {ex}");
            //}

            //return result;

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private MemPoolProto MempoolProtoFactory(TransactionProto tx)
        {
            return new()
            {
                Block = new InterpretedProto
                {
                    Hash = tx.ToKeyImage().ByteToHex(),
                    Node = _serfClient.P2PConnectionOptions.ClientId,
                    Round = 0,
                    Transaction = tx
                },
                Deps = new List<DepProto>()
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<bool> MempoolTxExist(TransactionProto tx)
        {
            var memPool = await _unitOfWork.MemPoolRepository.FirstOrDefaultAsync(x =>
                x.Block.Hash.Equals(tx.ToHash().ByteToHex()) &&
                x.Block.Transaction.Ver == tx.Ver &&
                x.Block.Node == _serfClient.P2PConnectionOptions.ClientId);

            return memPool != null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<bool> DeliveredTxExist(TransactionProto tx)
        {
            foreach (var vin in tx.Vin)
            {
                var exists = await _unitOfWork.DeliveredRepository
                    .FirstOrDefaultAsync(x => x.Transactions.Any(t => t.Vin.First().Key.K_Image.SequenceEqual(vin.Key.K_Image)));

                if (exists != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="message"></param>
        /// <param name="isError"></param>
        /// <returns></returns>
        private async Task<byte[]> Payload(TransactionProto tx, string message, bool isError)
        {
            byte[] data = CYPCore.Helper.Util.SerializeProto(tx);
            var payload = new PayloadProto
            {
                Error = isError,
                Message = message,
                Node = _serfClient.P2PConnectionOptions.ClientId,
                Payload = data,
                PublicKey = await _signingProvider.GePublicKey(_signingProvider.DefaultSigningKeyName),
                Signature = await _signingProvider.Sign(_signingProvider.DefaultSigningKeyName, CYPCore.Helper.Util.SHA384ManagedHash(data))
            };

            return CYPCore.Helper.Util.SerializeProto(payload);
        }
    }
}
