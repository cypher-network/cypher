// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.Extensions.Logging;

using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class DataProtectionPayloadRepository : Repository<DataProtectionPayloadProto>, IDataProtectionPayloadRepository
    {
        private const string TableDataProtectionPayload = "DataProtectionPayload";

        public string Table => TableDataProtectionPayload;

        public DataProtectionPayloadRepository(IStoredbContext storedbContext, ILogger logger)
            : base(storedbContext, logger)
        {
            SetTableType(TableDataProtectionPayload);
        }
    }
}
