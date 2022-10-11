// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using Dawn;
using MessagePack;
using Serilog;

namespace CypherNetwork.Persistence;

/// <summary>
/// </summary>
public interface IHashChainRepository : IRepository<Block>
{
    ValueTask<List<Block>> OrderByRangeAsync(Func<Block, ulong> selector, int skip, int take);
    new Task<bool> PutAsync(byte[] key, Block data);
    ulong Height { get; }
    ulong Count { get; }
}

/// <summary>
/// </summary>
public class HashChainRepository : Repository<Block>, IHashChainRepository
{
    private readonly ILogger _logger;
    private readonly IStoreDb _storeDb;
    private readonly ReaderWriterLockSlim _sync = new();

    /// <summary>
    /// </summary>
    /// <param name="storeDb"></param>
    /// <param name="logger"></param>
    public HashChainRepository(IStoreDb storeDb, ILogger logger)
        : base(storeDb, logger)
    {
        _storeDb = storeDb;
        _logger = logger.ForContext("SourceContext", nameof(HashChainRepository));

        SetTableName(StoreDb.HashChainTable.ToString());
        Height = (ulong)AsyncHelper.RunSync(GetBlockHeightAsync);
        Count = Height + 1;
    }

    /// <summary>
    /// 
    /// </summary>
    public ulong Height { get; private set; }

    /// <summary>
    /// 
    /// </summary>
    public ulong Count { get; private set; }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public new Task<bool> PutAsync(byte[] key, Block data)
    {
        Guard.Argument(key, nameof(key)).NotNull().MaxCount(64);
        Guard.Argument(data, nameof(data)).NotNull();
        if (data.Validate().Any()) return Task.FromResult(false);
        try
        {
            using (_sync.Write())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(GetTableNameAsString());
                _storeDb.Rocks.Put(StoreDb.Key(StoreDb.HashChainTable.ToString(), key),
                    MessagePackSerializer.Serialize(data), cf);
                Height = data.Height;
                Count++;
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while storing in database");
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// </summary>
    /// <param name="selector"></param>
    /// <param name="skip"></param>
    /// <param name="take"></param>
    /// <returns></returns>
    public ValueTask<List<Block>> OrderByRangeAsync(Func<Block, ulong> selector, int skip, int take)
    {
        Guard.Argument(selector, nameof(selector)).NotNull();
        Guard.Argument(skip, nameof(skip)).NotNegative();
        Guard.Argument(take, nameof(take)).NotNegative();
        try
        {
            using (_sync.Read())
            {
                var entries = IterateAsync().OrderBy(selector).Skip(skip).Take(take).ToListAsync();
                return entries;
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return default;
    }
}