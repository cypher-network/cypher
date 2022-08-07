// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Serilog;

namespace CypherNetwork.Persistence;

/// <summary>
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
/// </summary>
public class UnitOfWork : IUnitOfWork, IDisposable
{
    /// <summary>
    /// </summary>
    /// <param name="folderDb"></param>
    /// <param name="logger"></param>
    public UnitOfWork(string folderDb, ILogger logger)
    {
        StoreDb = new StoreDb(folderDb);
        var log = logger.ForContext("SourceContext", nameof(UnitOfWork));
        DataProtectionPayload = new DataProtectionRepository(StoreDb, log);
        HashChainRepository = new HashChainRepository(StoreDb, log);
    }

    public IStoreDb StoreDb { get; }

    public IXmlRepository DataProtectionKeys { get; }
    public IDataProtectionRepository DataProtectionPayload { get; }
    public IHashChainRepository HashChainRepository { get; }

    /// <summary>
    /// </summary>
    public void Dispose()
    {
        StoreDb.Rocks.Dispose();
    }
}