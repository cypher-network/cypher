// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Serilog;
using CYPCore.Models;

namespace CYPCore.Persistence
{
    public interface IMemPoolRepository : IRepository<MemPoolProto>
    {
    }

    public class MemPoolRepository : Repository<MemPoolProto>, IMemPoolRepository
    {
        private readonly ILogger _logger;

        public MemPoolRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(MemPoolRepository));

            SetTableName(StoreDb.MemoryPoolTable.ToString());
        }
    }
}