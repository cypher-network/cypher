// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using Dawn;
using MessagePack;
using Microsoft.IO;
using RocksDbSharp;
using Serilog;

namespace CypherNetwork.Persistence;

/// <summary>
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IRepository<T>
{
    Task<long> CountAsync();
    Task<long> GetBlockHeightAsync();
    Task<T> GetAsync(byte[] key);
    Task<T> GetAsync(Func<T, ValueTask<bool>> expression);
    void SetTableName(string tableName);
    string GetTableNameAsString();
    byte[] GetTableNameAsBytes();
    Task<bool> PutAsync(byte[] key, T data);
    Task<IList<T>> RangeAsync(long skip, int take);
    Task<T> LastAsync();
    ValueTask<List<T>> WhereAsync(Func<T, ValueTask<bool>> expression);
    Task<T> FirstAsync();
    ValueTask<List<T>> SelectAsync(Func<T, ValueTask<T>> selector);
    ValueTask<List<T>> SkipAsync(int skip);
    ValueTask<List<T>> TakeAsync(int take);
    bool Delete(byte[] key);
    Task<IList<T>> TakeLongAsync(ulong take);
    IAsyncEnumerable<T> IterateAsync();
}

/// <summary>
/// </summary>
/// <typeparam name="T"></typeparam>
public class Repository<T> : IRepository<T> where T : class, new()
{
    private readonly ILogger _logger;
    private readonly ReadOptions _readOptions;
    private readonly IStoreDb _storeDb;
    private readonly ReaderWriterLockSlim _sync = new();

    private string _tableName;
    private byte[] _tableNameBytes;
    /// <summary>
    /// </summary>
    /// <param name="storeDb"></param>
    /// <param name="logger"></param>
    protected Repository(IStoreDb storeDb, ILogger logger)
    {
        _storeDb = storeDb;
        _logger = logger.ForContext("SourceContext", nameof(Repository<T>));

        _readOptions = new ReadOptions();
        _readOptions
            .SetPrefixSameAsStart(true)
            .SetVerifyChecksums(false);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public async Task<long> GetBlockHeightAsync()
    {
        var height = await CountAsync() - 1;
        if (height < 0) height = 0;
        return height;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public Task<long> CountAsync()
    {
        long count = 0;
        try
        {
            using (_sync.Read())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
                unsafe
                {
                    fixed (byte* k = _tableNameBytes.AsSpan())
                    {
                        for (iterator.Seek(k, (ulong)_tableNameBytes.Length); iterator.Valid(); iterator.Next())
                        {
                            Interlocked.Increment(ref count);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return Task.FromResult(count);
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public async Task<T> GetAsync(byte[] key)
    {
        Guard.Argument(key, nameof(key)).NotNull().NotEmpty();
        try
        {
            using (_sync.Read())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                var value = _storeDb.Rocks.Get(StoreDb.Key(_tableName, key), cf, _readOptions);
                if (value is { })
                {
                    await using var stream = Util.Manager.GetStream(value.AsSpan()) as RecyclableMemoryStream;
                    var entry = await MessagePackSerializer.DeserializeAsync<T>(stream);
                    return entry;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    public async Task<T> GetAsync(Func<T, ValueTask<bool>> expression)
    {
        Guard.Argument(expression, nameof(expression)).NotNull();
        try
        {
            using (_sync.Read())
            {
                var first = IterateAsync().FirstOrDefaultAwaitAsync(expression);
                if (first.IsCompleted)
                {
                    var entry = await first;
                    return entry;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public bool Delete(byte[] key)
    {
        Guard.Argument(key, nameof(key)).NotNull().NotEmpty();
        try
        {
            using (_sync.Write())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                _storeDb.Rocks.Remove(StoreDb.Key(_tableName, key), cf);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while removing from database");
        }

        return false;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public async Task<T> FirstAsync()
    {
        try
        {
            using (_sync.Read())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
                iterator.SeekToFirst();
                if (iterator.Valid())
                {
                    await using var stream = Util.Manager.GetStream(iterator.Value().AsSpan()) as RecyclableMemoryStream;
                    var entry = await MessagePackSerializer.DeserializeAsync<T>(stream);
                    return entry;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public Task<bool> PutAsync(byte[] key, T data)
    {
        Guard.Argument(key, nameof(key)).NotNull().NotEmpty();
        Guard.Argument(data, nameof(data)).NotNull();
        try
        {
            using (_sync.Write())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                var buffer = MessagePackSerializer.Serialize(data);
                _storeDb.Rocks.Put(StoreDb.Key(_tableName, key), buffer, cf);
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
    /// <param name="tableName"></param>
    public void SetTableName(string tableName)
    {
        Guard.Argument(tableName, nameof(tableName)).NotNull().NotEmpty().NotWhiteSpace();
        using (_sync.Write())
        {
            _tableName = tableName;
            _tableNameBytes = tableName.ToBytes();
        }
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public string GetTableNameAsString()
    {
        using (_sync.Read())
        {
            return _tableName;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public byte[] GetTableNameAsBytes()
    {
        using (_sync.Read())
        {
            return _tableNameBytes;
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="skip"></param>
    /// <param name="take"></param>
    /// <returns></returns>
    public async Task<IList<T>> RangeAsync(long skip, int take)
    {
        Guard.Argument(skip, nameof(skip)).Negative();
        Guard.Argument(take, nameof(take)).Negative();
        IList<T> entries = new List<T>(take);
        try
        {
            using (_sync.Read())
            {
                long iSkip = 0;
                var iTake = 0;
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
                for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                {
                    iSkip++;
                    if (skip != 0)
                        if (iSkip % skip != 0)
                            continue;
                    await using var stream = Util.Manager.GetStream(iterator.Value().AsSpan()) as RecyclableMemoryStream;
                    entries.Add(await MessagePackSerializer.DeserializeAsync<T>(stream));
                    iTake++;
                    if (iTake % take == 0) break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return entries;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public async Task<T> LastAsync()
    {
        try
        {
            using (_sync.Read())
            {
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
                iterator.SeekToLast();
                if (iterator.Valid())
                {
                    await using var stream = Util.Manager.GetStream(iterator.Value().AsSpan()) as RecyclableMemoryStream;
                    var entry = await MessagePackSerializer.DeserializeAsync<T>(stream);
                    return entry;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return null;
    }

    /// <summary>
    /// </summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    public ValueTask<List<T>> WhereAsync(Func<T, ValueTask<bool>> expression)
    {
        Guard.Argument(expression, nameof(expression)).NotNull();
        try
        {
            using (_sync.Read())
            {
                var entries = IterateAsync().WhereAwait(expression).ToListAsync();
                return entries;
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return default;
    }

    /// <summary>
    /// </summary>
    /// <param name="selector"></param>
    /// <returns></returns>
    public ValueTask<List<T>> SelectAsync(Func<T, ValueTask<T>> selector)
    {
        Guard.Argument(selector, nameof(selector)).NotNull();
        try
        {
            using (_sync.Read())
            {
                var entries = IterateAsync().SelectAwait(selector).ToListAsync();
                return entries;
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return default;
    }

    /// <summary>
    /// </summary>
    /// <param name="skip"></param>
    /// <returns></returns>
    public ValueTask<List<T>> SkipAsync(int skip)
    {
        Guard.Argument(skip, nameof(skip)).NotNegative();
        try
        {
            using (_sync.Read())
            {
                var entries = IterateAsync().Skip(skip).ToListAsync();
                return entries;
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return default;
    }

    /// <summary>
    /// </summary>
    /// <param name="take"></param>
    /// <returns></returns>
    public ValueTask<List<T>> TakeAsync(int take)
    {
        Guard.Argument(take, nameof(take)).NotNegative();
        try
        {
            using (_sync.Read())
            {
                var entries = IterateAsync().Take(take).ToListAsync();
                return entries;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error while reading database");
        }

        return default;
    }

    /// <summary>
    /// </summary>
    /// <param name="take"></param>
    /// <returns></returns>
    public async Task<IList<T>> TakeLongAsync(ulong take)
    {
        Guard.Argument(take, nameof(take)).NotNegative();
        IList<T> entries = new List<T>();
        try
        {
            using (_sync.Read())
            {
                take = take == 0 ? 1 : take;
                ulong iTake = 0;
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
                for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                {
                    await using var stream = Util.Manager.GetStream(iterator.Value().AsSpan()) as RecyclableMemoryStream;
                    entries.Add(await MessagePackSerializer.DeserializeAsync<T>(stream));
                    iTake++;
                    if (iTake % take == 0) break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Error while reading database");
        }

        return entries;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
#pragma warning disable 1998
    public async IAsyncEnumerable<T> IterateAsync()
#pragma warning restore 1998
    {
        var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
        using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
        for (iterator.Seek(_tableNameBytes); iterator.Valid(); iterator.Next())
        {
            if (!iterator.Valid()) continue;
            await using var stream = Util.Manager.GetStream(iterator.Value().AsSpan()) as RecyclableMemoryStream;
            yield return await MessagePackSerializer.DeserializeAsync<T>(stream);
        }
    }
}