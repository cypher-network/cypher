// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Models;
using Serilog;

namespace CYPCore.Persistence
{
    public interface IHashChainRepository : IRepository<BlockHeaderProto>
    {
    }

    public class HashChainRepository : Repository<BlockHeaderProto>, IHashChainRepository
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;

        public HashChainRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            _storeDb = storeDb;
            _logger = logger.ForContext("SourceContext", nameof(HashChainRepository));

            SetTableName(StoreDb.HashChainTable.ToString());
        }
    }
}