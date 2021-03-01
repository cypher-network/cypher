// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.DataProtection.Repositories;

using Serilog;

namespace CYPCore.Persistence
{
    public interface IUnitOfWork
    {
        IStoreDb StoreDb { get; }
        IXmlRepository DataProtectionKeys { get; }
        IDataProtectionRepository DataProtectionPayload { get; }
        IInterpretedRepository InterpretedRepository { get; }
        IMemPoolRepository MemPoolRepository { get; }
        IStagingRepository StagingRepository { get; }
        IDeliveredRepository DeliveredRepository { get; }
        ISeenBlockHeaderRepository SeenBlockHeaderRepository { get; }
    }

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

        public UnitOfWork(string folderDb, ILogger logger)
        {
            StoreDb = new StoreDb(folderDb);

            _logger = logger.ForContext("SourceContext", nameof(UnitOfWork));

            DataProtectionPayload = new DataProtectionRepository(StoreDb, logger);
            DeliveredRepository = new DeliveredRepository(StoreDb, logger);
            InterpretedRepository = new InterpretedRepository(StoreDb, logger);
            MemPoolRepository = new MemPoolRepository(StoreDb, logger);
            StagingRepository = new StagingRepository(StoreDb, logger);
            SeenBlockHeaderRepository = new SeenBlockHeaderRepository(StoreDb, logger);
        }
    }
}