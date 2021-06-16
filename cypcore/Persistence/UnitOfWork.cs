// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.DataProtection.Repositories;

using Serilog;
using Serilog.Core;

namespace CYPCore.Persistence
{
    public interface IUnitOfWork
    {
        IStoreDb StoreDb { get; }
        IXmlRepository DataProtectionKeys { get; }
        IDataProtectionRepository DataProtectionPayload { get; }
        IDeliveredRepository DeliveredRepository { get; }
        ITransactionRepository TransactionRepository { get; }
        IBlockGraphRepository BlockGraphRepository { get; }
        IKeyImageRepository KeyImageRepository { get; }
        IHashChainRepository HashChainRepository { get; }
    }

    public class UnitOfWork : IUnitOfWork
    {
        public IStoreDb StoreDb { get; }

        public IXmlRepository DataProtectionKeys { get; }
        public IDataProtectionRepository DataProtectionPayload { get; }
        public IDeliveredRepository DeliveredRepository { get; }
        public ITransactionRepository TransactionRepository { get; }
        public IBlockGraphRepository BlockGraphRepository { get; }
        public IKeyImageRepository KeyImageRepository { get; }
        public IHashChainRepository HashChainRepository { get; }

        private readonly ILogger _logger;

        public UnitOfWork(string folderDb, ILogger logger)
        {
            StoreDb = new StoreDb(folderDb);

            _logger = logger.ForContext("SourceContext", nameof(UnitOfWork));

            DataProtectionPayload = new DataProtectionRepository(StoreDb, logger);
            DeliveredRepository = new DeliveredRepository(StoreDb, logger);
            TransactionRepository = new TransactionRepository(StoreDb, logger);
            BlockGraphRepository = new BlockGraphRepository(StoreDb, logger);
            KeyImageRepository = new KeyImageRepository(StoreDb, logger);
            HashChainRepository = new HashChainRepository(StoreDb, logger);
        }
    }
}