// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using Dawn;
using Serilog;
using RocksDbSharp;
using MessagePack;

namespace CYPCore.Persistence
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IRepository<T>
    {
        Task<long> CountAsync();
        Task<long> GetBlockHeightAsync();
        Task<T> GetAsync(byte[] key);
        Task<T> GetAsync(Func<T, ValueTask<bool>> expression);
        void SetTableName(string tableName);
        Task<bool> PutAsync(byte[] key, T data);
        Task<IList<T>> RangeAsync(long skip, int take);
        Task<T> LastAsync();
        ValueTask<List<T>> WhereAsync(Func<T, ValueTask<bool>> expression);
        Task<T> FirstAsync();
        ValueTask<List<T>> SelectAsync(Func<T, ValueTask<T>> selector);
        ValueTask<List<T>> SkipAsync(int skip);
        ValueTask<List<T>> TakeAsync(int take);
        Task<bool> RemoveAsync(byte[] key);
        Task<IList<T>> TakeLongAsync(long take);
        IAsyncEnumerable<T> Iterate();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Repository<T> : IRepository<T> where T : class
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;
        private readonly ReadOptions _readOptions;
        private readonly ReaderWriterLockSlim _sync = new();

        private string _tableName;

        /// <summary>
        /// 
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
        public Task<long> GetBlockHeightAsync()
        {
            var height = CountAsync().GetAwaiter().GetResult() - 1;
            return Task.FromResult(height);
        }

        /// <summary>
        /// 
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

                    for (iterator.Seek(_tableName.ToBytes()); iterator.Valid(); iterator.Next())
                    {
                        if (new string(iterator.Key().FromBytes()).StartsWith(new string(_tableName)))
                        {
                            Interlocked.Increment(ref count);
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
        /// 
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
                        var entry = await DeserializeAsync(value);
                        return entry;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return default;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public Task<T> GetAsync(Func<T, ValueTask<bool>> expression)
        {
            Guard.Argument(expression, nameof(expression)).NotNull();
            try
            {
                using (_sync.Read())
                {
                    var first = Iterate().FirstOrDefaultAwaitAsync(expression);
                    if (first.IsCompleted)
                    {
                        var entry = first.Result;
                        return Task.FromResult(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return Task.FromResult<T>(null);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public Task<bool> RemoveAsync(byte[] key)
        {
            Guard.Argument(key, nameof(key)).NotNull().MaxCount(64);
            try
            {
                using (_sync.Write())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    _storeDb.Rocks.Remove(StoreDb.Key(_tableName, key), cf);
                    return Task.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while removing from database");
            }

            return Task.FromResult(false);
        }

        /// <summary>
        /// 
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
                        var entry = await DeserializeAsync(iterator.Value());
                        return entry;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return default;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task<bool> PutAsync(byte[] key, T data)
        {
            Guard.Argument(key, nameof(key)).NotNull().MaxCount(64);
            Guard.Argument(data, nameof(data)).NotNull();
            try
            {
                using (_sync.Write())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    var buffer = await SerializeAsync(data);
                    _storeDb.Rocks.Put(StoreDb.Key(_tableName, key), buffer, cf);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while storing in database");
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        public void SetTableName(string tableName)
        {
            Guard.Argument(tableName, nameof(tableName)).NotNull().NotEmpty().NotWhiteSpace();
            using (_sync.Write())
            {
                _tableName = tableName;
            }
        }

        /// <summary>
        /// 
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
                        {
                            if (iSkip % skip != 0) continue;
                        }

                        entries.Add(await DeserializeAsync(iterator.Value()));
                        iTake++;
                        if (iTake % take == 0)
                        {
                            break;
                        }
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
        /// 
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
                        var entry = await DeserializeAsync(iterator.Value());
                        return entry;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return default;
        }

        /// <summary>
        /// 
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
                    var entries = Iterate().WhereAwait(expression).ToListAsync();
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
        /// 
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
                    var entries = Iterate().SelectAwait(selector).ToListAsync();
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
        /// 
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
                    var entries = Iterate().Skip(skip).ToListAsync();
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
        /// 
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
                    var entries = Iterate().Take(take).ToListAsync();
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
        /// 
        /// </summary>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<IList<T>> TakeLongAsync(long take)
        {
            Guard.Argument(take, nameof(take)).NotNegative();
            IList<T> entries = new List<T>();
            try
            {
                using (_sync.Read())
                {
                    take = take == 0 ? 1 : take;
                    var iTake = 0;
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
                    for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                    {
                        entries.Add(await DeserializeAsync(iterator.Value()));
                        iTake++;
                        if (iTake % take == 0)
                        {
                            break;
                        }
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
        /// 
        /// </summary>
        /// <returns></returns>
#pragma warning disable 1998
        public async IAsyncEnumerable<T> Iterate()
#pragma warning restore 1998
        {
            var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
            using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);
            for (iterator.Seek(_tableName.ToBytes()); iterator.Valid(); iterator.Next())
            {
                if (!new string(iterator.Key().FromBytes()).StartsWith(new string(_tableName))) continue;
                if (!iterator.Valid()) continue;
                yield return await DeserializeAsync(iterator.Value());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private async Task<byte[]> SerializeAsync(T data)
        {
            await using var stream = new MemoryStream();
            MessagePackSerializer.SerializeAsync(stream, data).Wait();
            return stream.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private async Task<T> DeserializeAsync(byte[] data)
        {
            await using var stream = new MemoryStream(data);
            return await MessagePackSerializer.DeserializeAsync<T>(stream);
        }
    }
}