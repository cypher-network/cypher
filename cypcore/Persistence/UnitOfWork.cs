// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;

namespace CYPCore.Persistence
{
    public class UnitOfWork : IUnitOfWork
    {
        public IStoredb Storedb { get; }
        public IXmlRepository DataProtectionKeys { get; }
        public IDataProtectionPayloadRepository DataProtectionPayload { get; }
        public IInterpretedRepository InterpretedRepository { get; }
        public IMemPoolRepository MemPoolRepository { get; }
        public IStagingRepository StagingRepository { get; }
        public IDeliveredRepository DeliveredRepository { get; }
        public ISeenBlockHeaderRepository SeenBlockHeaderRepository { get; }

        private readonly ILogger _logger;

        public UnitOfWork(string folderDb, ILogger<UnitOfWork> logger)
        {
            Storedb = new Storedb(folderDb);

            _logger = logger;

            DataProtectionPayload = new DataProtectionPayloadRepository(Storedb, logger);
            DeliveredRepository = new DeliveredRepository(Storedb, logger);
            InterpretedRepository = new InterpretedRepository(Storedb, logger);
            MemPoolRepository = new MemPoolRepository(Storedb, logger);
            StagingRepository = new StagingRepository(Storedb, logger);
            SeenBlockHeaderRepository = new SeenBlockHeaderRepository(Storedb, logger);
        }
    }
}
