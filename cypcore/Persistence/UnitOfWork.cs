// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;

namespace CYPCore.Persistence
{
    public class UnitOfWork : IUnitOfWork
    {
        public IStoreDb StoreDb { get; }

        public IXmlRepository DataProtectionKeys { get; }
        public IDataProtectionRepository DataProtectionPayload { get; }
        public IInterpretedRepository InterpretedRepository { get; }
        public IMemPoolRepository MemPoolRepository { get; }
        public IStagingRepository StagingRepository { get; }
        public IDeliveredRepository DeliveredRepository { get; }
        public ISeenBlockHeaderRepository SeenBlockHeaderRepository { get; }

        private readonly ILogger _logger;

        public UnitOfWork(string folderDb, ILogger<UnitOfWork> logger)
        {
            StoreDb = new StoreDb(folderDb);

            _logger = logger;

            DataProtectionPayload = new DataProtectionRepository(StoreDb, logger);
            DeliveredRepository = new DeliveredRepository(StoreDb, logger);
            InterpretedRepository = new InterpretedRepository(StoreDb, logger);
            MemPoolRepository = new MemPoolRepository(StoreDb, logger);
            StagingRepository = new StagingRepository(StoreDb, logger);
            SeenBlockHeaderRepository = new SeenBlockHeaderRepository(StoreDb, logger);
        }
    }
}