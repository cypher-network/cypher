// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Models;
using Serilog;

namespace CYPCore.Persistence
{
    public interface ITransactionRepository : IRepository<Transaction>
    {
    }

    public class TransactionRepository : Repository<Transaction>, ITransactionRepository
    {
        private readonly ILogger _logger;

        public TransactionRepository(IStoreDb storeDb, ILogger logger) : base(storeDb, logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(TransactionRepository));
            SetTableName(StoreDb.TransactionTable.ToString());
        }
    }
}