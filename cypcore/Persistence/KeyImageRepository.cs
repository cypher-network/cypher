using CYPCore.Models;
using Serilog;

namespace CYPCore.Persistence
{
    public interface IKeyImageRepository : IRepository<KeyImage>
    {
    }

    public class KeyImageRepository : Repository<KeyImage>, IKeyImageRepository
    {
        private readonly ILogger _logger;

        public KeyImageRepository(IStoreDb storeDb, ILogger logger) : base(storeDb, logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(KeyImageRepository));
            SetTableName(StoreDb.KeyImageTable.ToString());
        }
    }
}