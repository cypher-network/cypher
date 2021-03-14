using CYPCore.Consensus.Models;
using Serilog;

namespace CYPCore.Persistence
{
    public interface IBlockGraphRepository : IRepository<BlockGraph>
    {
    }

    public class BlockGraphRepository : Repository<BlockGraph>, IBlockGraphRepository
    {
        public BlockGraphRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            SetTableName(StoreDb.BlockGraphTable.ToString());
        }
    }
}