// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using Serilog;
using CYPCore.Extentions;
using FlatSharp;
using RocksDbSharp;

namespace CYPCore.Persistence
{
    public interface IRepository<T>
    {
        Task<long> CountAsync();
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

    public class Repository<T> : IRepository<T> where T : class
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;
        private readonly object _locker = new();
        private readonly ReadOptions _readOptions;
        private readonly ReaderWriterLockSlim _sync = new();

        private string _tableName;

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
                        if (new string(iterator.Key().ToStr()).StartsWith(new string(_tableName)))
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
        public Task<T> GetAsync(byte[] key)
        {
            T entry = default;

            try
            {
                using (_sync.Read())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    var value = _storeDb.Rocks.Get(StoreDb.Key(_tableName, key), cf, _readOptions);
                    if (value != null)
                    {
                        entry = FlatBufferSerializer.Default.Parse<T>(value);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return Task.FromResult(entry);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public Task<T> GetAsync(Func<T, ValueTask<bool>> expression)
        {
            T entry = default;

            try
            {
                using (_sync.Read())
                {
                    var first = Iterate().FirstOrDefaultAwaitAsync(expression);
                    if (first.IsCompleted)
                    {
                        entry = first.Result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return Task.FromResult(entry);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public Task<bool> RemoveAsync(byte[] key)
        {
            var removed = false;

            try
            {
                using (_sync.Write())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    _storeDb.Rocks.Remove(StoreDb.Key(_tableName, key), cf);

                    removed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while removing from database");
            }

            return Task.FromResult(removed);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task<T> FirstAsync()
        {
            T entry = default;

            try
            {
                using (_sync.Read())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);

                    iterator.SeekToFirst();
                    if (iterator.Valid())
                    {
                        entry = FlatBufferSerializer.Default.Parse<T>(iterator.Value());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return Task.FromResult(entry);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public Task<bool> PutAsync(byte[] key, T data)
        {
            var saved = false;

            try
            {
                using (_sync.Write())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    var maxBytesNeeded = FlatBufferSerializer.Default.GetMaxSize(data);
                    var buffer = new byte[maxBytesNeeded];

                    FlatBufferSerializer.Default.Serialize(data, buffer);

                    _storeDb.Rocks.Put(StoreDb.Key(_tableName, key), buffer, cf);
                    saved = true;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while storing in database");
            }

            return Task.FromResult(saved);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        public void SetTableName(string tableName)
        {
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
        public Task<IList<T>> RangeAsync(long skip, int take)
        {
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

                        entries.Add(FlatBufferSerializer.Default.Parse<T>(iterator.Value()));

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

            return Task.FromResult(entries);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task<T> LastAsync()
        {
            T entry = default;

            try
            {
                using (_sync.Read())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    using var iterator = _storeDb.Rocks.NewIterator(cf, _readOptions);

                    iterator.SeekToLast();
                    if (iterator.Valid())
                    {
                        entry = FlatBufferSerializer.Default.Parse<T>(iterator.Value());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return Task.FromResult(entry);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public ValueTask<List<T>> WhereAsync(Func<T, ValueTask<bool>> expression)
        {
            ValueTask<List<T>> entries = default;

            try
            {
                using (_sync.Read())
                {
                    entries = Iterate().WhereAwait(expression).ToListAsync();
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
        /// <param name="selector"></param>
        /// <returns></returns>
        public ValueTask<List<T>> SelectAsync(Func<T, ValueTask<T>> selector)
        {
            ValueTask<List<T>> entries = default;

            try
            {
                using (_sync.Read())
                {
                    entries = Iterate().SelectAwait(selector).ToListAsync();
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
        /// <param name="skip"></param>
        /// <returns></returns>
        public ValueTask<List<T>> SkipAsync(int skip)
        {
            ValueTask<List<T>> entries = default;

            try
            {
                using (_sync.Read())
                {
                    entries = Iterate().Skip(skip).ToListAsync();
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
        /// <param name="take"></param>
        /// <returns></returns>
        public ValueTask<List<T>> TakeAsync(int take)
        {
            ValueTask<List<T>> entries = default;

            try
            {
                using (_sync.Read())
                {
                    entries = Iterate().Take(take).ToListAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while reading database");
            }

            return entries;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="take"></param>
        /// <returns></returns>
        public Task<IList<T>> TakeLongAsync(long take)
        {
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
                        entries.Add(FlatBufferSerializer.Default.Parse<T>(iterator.Value()));

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

            return Task.FromResult(entries);
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
                if (!new string(iterator.Key().ToStr()).StartsWith(new string(_tableName))) continue;
                if (!iterator.Valid()) continue;

                yield return FlatBufferSerializer.Default.Parse<T>(iterator.Value()); ;
            }
        }
    }
}