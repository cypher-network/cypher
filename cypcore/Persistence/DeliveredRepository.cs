// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Serilog;
using CYPCore.Models;

namespace CYPCore.Persistence
{
    public interface IDeliveredRepository : IRepository<Block>
    {
    }

    public class DeliveredRepository : Repository<Block>, IDeliveredRepository
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;

        public DeliveredRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            _storeDb = storeDb;
            _logger = logger.ForContext("SourceContext", nameof(DeliveredRepository));

            SetTableName(StoreDb.DeliveredTable.ToString());
        }
    }
}