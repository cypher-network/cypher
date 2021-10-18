// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using Microsoft.AspNetCore.DataProtection.Repositories;

using Serilog;

namespace CYPCore.Persistence
{
    /// <summary>
    /// 
    /// </summary>
    public interface IUnitOfWork
    {
        IStoreDb StoreDb { get; }
        IXmlRepository DataProtectionKeys { get; }
        IDataProtectionRepository DataProtectionPayload { get; }
        IHashChainRepository HashChainRepository { get; }
        void Dispose();
    }

    /// <summary>
    /// 
    /// </summary>
    public class UnitOfWork : IUnitOfWork, IDisposable
    {
        public IStoreDb StoreDb { get; }

        public IXmlRepository DataProtectionKeys { get; }
        public IDataProtectionRepository DataProtectionPayload { get; }
        public IHashChainRepository HashChainRepository { get; }

        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="folderDb"></param>
        /// <param name="logger"></param>
        public UnitOfWork(string folderDb, ILogger logger)
        {
            StoreDb = new StoreDb(folderDb);
            _logger = logger.ForContext("SourceContext", nameof(UnitOfWork));
            DataProtectionPayload = new DataProtectionRepository(StoreDb, logger);
            HashChainRepository = new HashChainRepository(StoreDb, logger);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            StoreDb.Rocks.Dispose();
        }
    }
}