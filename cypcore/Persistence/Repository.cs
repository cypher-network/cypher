// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using CYPCore.Extentions;

namespace CYPCore.Persistence
{
    public class Repository<T> : IRepository<T>
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;
        private readonly object _locker = new();

        private string _tableName;

        protected Repository(IStoreDb storeDb, ILogger logger)
        {
            _storeDb = storeDb;
            _logger = logger;
        }

        public Task<long> CountAsync()
        {
            long count = 0;

            try
            {
                lock (_locker)
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    var readOptions = new RocksDbSharp.ReadOptions();

                    using var iterator = _storeDb.Rocks.NewIterator(cf, readOptions);

                    for (iterator.Seek(_tableName.ToBytes()); iterator.Valid(); iterator.Next())
                    {
                        if (new string(iterator.Key().ToStr()).StartsWith(new string(_tableName)))
                        {
                            Interlocked.Increment(ref count);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.CountAsync >>>: {e}");
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
                lock (_locker)
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    var value = _storeDb.Rocks.Get(StoreDb.Key(_tableName, key), cf);
                    if (value != null)
                    {
                        entry = Helper.Util.DeserializeProto<T>(value);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.GetAsync >>>: {e}");
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
                lock (_locker)
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    _storeDb.Rocks.Remove(StoreDb.Key(_tableName, key), cf);

                    removed = true;
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.GetAsync >>>: {e}");
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
                var first = Iterate().FirstOrDefaultAsync();
                if (first.IsCompleted)
                {
                    entry = first.Result;
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.FirstAsync >>>: {e}");
            }

            return Task.FromResult(entry);
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public Task<T> FirstAsync(Func<T, ValueTask<bool>> expression)
        {
            T entry = default;

            try
            {
                var first = Iterate().FirstOrDefaultAwaitAsync(expression);
                if (first.IsCompleted)
                {
                    entry = first.Result;
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.FirstAsync(Func) >>>: {e}");
            }

            return Task.FromResult(entry);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public Task<T> LastAsync(Func<T, ValueTask<bool>> expression)
        {
            T entry = default;

            try
            {
                var last = Iterate().LastOrDefaultAwaitAsync(expression);
                if (last.IsCompleted)
                {
                    entry = last.Result;
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.LastAsync >>>: {e}");
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
                lock (_locker)
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    _storeDb.Rocks.Put(StoreDb.Key(_tableName, key), Helper.Util.SerializeProto(data), cf);
                    saved = true;
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.PutAsync >>>: {e}");
            }

            return Task.FromResult(saved);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        public void SetTableName(string tableName)
        {
            lock (_locker)
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
        public Task<HashSet<T>> RangeAsync(long skip, int take)
        {
            var entries = new HashSet<T>(take);

            try
            {
                lock (_locker)
                {
                    long iSkip = 0;
                    var iTake = 0;
                    
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    var readOptions = new RocksDbSharp.ReadOptions();

                    using var iterator = _storeDb.Rocks.NewIterator(cf, readOptions);

                    for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                    {
                        if (iSkip % skip == 0)
                        {
                            if (iTake % take == 0)
                            {
                                break;
                            }
                            
                            entries.Add(Helper.Util.DeserializeProto<T>(iterator.Value()));
                            iTake++;
                            
                            continue;
                        }
                        
                        iSkip++;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.RangeAsync >>>: {e}");
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
                lock (_locker)
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                    var readOptions = new RocksDbSharp.ReadOptions();

                    using var iterator = _storeDb.Rocks.NewIterator(cf, readOptions);

                    iterator.SeekToLast();
                    if (iterator.Valid())
                    {
                        entry = Helper.Util.DeserializeProto<T>(iterator.Value());
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.RangeAsync >>>: {e}");
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
                entries = Iterate().WhereAwait(expression).ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.WhereAsync >>>: {e}");
            }

            return entries;
        }
        
        public ValueTask<List<T>> SelectAsync(Func<T, ValueTask<T>> selector)
        {
            ValueTask<List<T>> entries = default;

            try
            {
                entries = Iterate().SelectAwait(selector).ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.SelectAsync >>>: {e}");
            }

            return entries;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public ValueTask<List<T>> SkipAsync(int skip)
        {
            ValueTask<List<T>> entries = default;

            try
            {
                entries = Iterate().Skip(skip).ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.SkipAsync >>>: {e}");
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
                entries = Iterate().Take(take).ToListAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"<<< Repository.TakeAsync >>>: {e}");
            }

            return entries;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
#pragma warning disable 1998
        private async IAsyncEnumerable<T> Iterate()
#pragma warning restore 1998
        {
            lock (_locker)
            {
                var cf = _storeDb.Rocks.GetColumnFamily(_tableName);
                var readOptions = new RocksDbSharp.ReadOptions();

                using var iterator = _storeDb.Rocks.NewIterator(cf, readOptions);

                for (iterator.Seek(_tableName.ToBytes()); iterator.Valid(); iterator.Next())
                {
                    if (!new string(iterator.Key().ToStr()).StartsWith(new string(_tableName))) continue;
                    if (iterator.Valid())
                    {
                        yield return Helper.Util.DeserializeProto<T>(iterator.Value());
                    }
                }
            }
        }
    }
}