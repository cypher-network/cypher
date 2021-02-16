using System;

using Microsoft.Extensions.Logging;

using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class SeenBlockHeaderRepository : Repository<SeenBlockHeaderProto>, ISeenBlockHeaderRepository
    {
        private readonly IStoredb _storedb;
        private readonly ILogger _logger;

        public SeenBlockHeaderRepository(IStoredb storedb, ILogger logger)
            : base(storedb, logger)
        {
            _storedb = storedb;
            _logger = logger;
        }
    }
}
