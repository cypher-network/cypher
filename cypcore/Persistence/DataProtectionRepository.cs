// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Serilog;

using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class DataProtectionRepository : Repository<DataProtectionProto>, IDataProtectionRepository
    {
        public DataProtectionRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            SetTableName(StoreDb.DataProtectionTable.ToString());
        }
    }
}
