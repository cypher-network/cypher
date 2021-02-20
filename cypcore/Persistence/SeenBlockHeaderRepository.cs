using Serilog;

using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class SeenBlockHeaderRepository : Repository<SeenBlockHeaderProto>, ISeenBlockHeaderRepository
    {
        public SeenBlockHeaderRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            SetTableName(StoreDb.SeenBlockHeaderTable.ToString());
        }
    }
}
