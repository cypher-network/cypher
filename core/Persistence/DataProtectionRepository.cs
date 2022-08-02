// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Models;
using Dawn;
using MessagePack;
using Serilog;

namespace CypherNetwork.Persistence;

/// <summary>
/// </summary>
public interface IDataProtectionRepository : IRepository<DataProtection>
{
    new Task<bool> PutAsync(byte[] key, DataProtection data);
}

/// <summary>
/// </summary>
public class DataProtectionRepository : Repository<DataProtection>, IDataProtectionRepository
{
    private readonly ILogger _logger;
    private readonly IStoreDb _storeDb;
    private readonly ReaderWriterLockSlim _sync = new();

    /// <summary>
    /// </summary>
    /// <param name="storeDb"></param>
    /// <param name="logger"></param>
    public DataProtectionRepository(IStoreDb storeDb, ILogger logger)
        : base(storeDb, logger)
    {
        _storeDb = storeDb;
        _logger = logger;
        SetTableName(StoreDb.DataProtectionTable.ToString());
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public new Task<bool> PutAsync(byte[] key, DataProtection data)
    {
        Guard.Argument(key, nameof(key)).NotNull().MaxCount(32);
        Guard.Argument(data, nameof(data)).NotNull();
        var saved = false;
        try
        {
            using (_sync.Write())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(GetTableNameAsString());
                var buffer = MessagePackSerializer.Serialize(data);
                _storeDb.Rocks.Put(StoreDb.Key(StoreDb.DataProtectionTable.ToString(), key), buffer, cf);
                saved = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while storing in database");
        }

        return Task.FromResult(saved);
    }
}