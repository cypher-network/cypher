// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;

namespace CYPCore.Persistence
{
    public class UnitOfWork : IUnitOfWork
    {
        public IStoredbContext StoredbContext { get; }
        public IXmlRepository DataProtectionKeys { get; }
        public IDataProtectionPayloadRepository DataProtectionPayload { get; }
        public IInterpretedRepository InterpretedRepository { get; }
        public IMemPoolRepository MemPoolRepository { get; }
        public IStagingRepository StagingRepository { get; }
        public IDeliveredRepository DeliveredRepository { get; }

        private readonly ILogger _logger;

        public UnitOfWork(IStoredbContext storedbContext, ILogger<UnitOfWork> logger)
        {
            StoredbContext = storedbContext;

            _logger = logger;

            DataProtectionKeys = new DataProtectionKeyRepository(storedbContext);
            DataProtectionPayload = new DataProtectionPayloadReposittory(storedbContext, logger);
            DeliveredRepository = new DeliveredRepository(storedbContext, logger);
            InterpretedRepository = new InterpretedRepository(storedbContext, logger);
            MemPoolRepository = new MemPoolRepository(storedbContext, logger);
            StagingRepository = new StagingRepository(storedbContext, logger);
        }

        public IGenericRepository<T> GenericRepositoryFactory<T>()
        {
            return new GenericRepository<T>(StoredbContext, _logger);
        }
    }
}
