// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Serilog;

using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class StagingRepository : Repository<StagingProto>, IStagingRepository
    {
        public StagingRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            SetTableName(StoreDb.StagingTable.ToString());
        }
    }
}
