// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using FASTER.core;


namespace CYPCore.Persistence
{
    public class Repository<TEntity> : IRepository<TEntity>
    {
        private readonly IStoredbContext _storedbContext;
        private readonly ILogger _logger;

        private string _tableType;

        public Repository(IStoredbContext storedbContext, ILogger logger)
        {
            _storedbContext = storedbContext;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="table"></param>
        public void SetTableType(string table)
        {
            _tableType = table;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public Task<TEntity> GetAsync(byte[] key)
        {
            TEntity entity = default;

            try
            {
                using var session = _storedbContext.Store.Database.NewSession(new StoreFunctions());

                StoreInput input = new();
                StoreOutput output = new();
                StoreContext context = new();
                StoreKey blockKey = new() { tableType = _tableType, key = key };

                Status readStatus = session.Read(ref blockKey, ref input, ref output, context, 1);

                if (readStatus == Status.PENDING)
                {
                    session.CompletePending(true);
                    context.FinalizeRead(ref readStatus, ref output);
                }

                if (readStatus == Status.OK)
                {
                    entity = Helper.Util.DeserializeProto<TEntity>(output.value.value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.GetAsync >>>: {ex}");
            }

            return Task.FromResult(entity);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public Task<TEntity> PutAsync(TEntity entity, byte[] key)
        {
            TEntity entityPut = default;

            try
            {
                using var session = _storedbContext.Store.Database.NewSession(new StoreFunctions());

                StoreKey upsertKey = new() { tableType = _tableType, key = key };
                StoreValue upsertvalue = new() { value = Helper.Util.SerializeProto(entity) };
                StoreContext context = new();

                var addStatus = session.Upsert(ref upsertKey, ref upsertvalue, context, 1);
                if (addStatus == Status.OK)
                {
                    entityPut = entity;
                }

                session.CompletePending(true);

                _storedbContext.Store.Checkpoint();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< StoredbContext.SaveOrUpdate >>>: {ex}");
            }

            return Task.FromResult(entityPut);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task<int> CountAsync()
        {
            int count = 0;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                ValueTask<int> total = iterateAsync.Iterate().CountAsync();

                if (total.IsCompleted)
                {
                    count = total.Result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.CountAsync >>>: {ex}");
            }

            return Task.FromResult(count);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public ValueTask<List<TEntity>> WhereAsync(Func<TEntity, ValueTask<bool>> expression)
        {
            ValueTask<List<TEntity>> entities = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                entities = iterateAsync.Iterate().WhereAwait(expression).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.WhereAsync >>>: {ex}");
            }

            return entities;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task<TEntity> FirstOrDefaultAsync()
        {
            TEntity entity = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                ValueTask<TEntity> first = iterateAsync.Iterate().FirstOrDefaultAsync();

                if (first.IsCompleted)
                {
                    entity = first.Result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.FirstOrDefaultAsync >>>: {ex}");
            }

            return Task.FromResult(entity);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public Task<TEntity> FirstOrDefaultAsync(Func<TEntity, ValueTask<bool>> expression)
        {
            TEntity entity = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                ValueTask<TEntity> first = iterateAsync.Iterate().FirstOrDefaultAwaitAsync(expression);

                if (first.IsCompleted)
                {
                    entity = first.Result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.FirstOrDefaultAsync(Func) >>>: {ex}");
            }

            return Task.FromResult(entity);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public Task<TEntity> LastOrDefaultAsync(Func<TEntity, ValueTask<bool>> expression)
        {
            TEntity entity = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                ValueTask<TEntity> last = iterateAsync.Iterate().LastOrDefaultAwaitAsync(expression);

                if (last.IsCompleted)
                {
                    entity = last.Result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.LastOrDefaultAsync >>>: {ex}");
            }

            return Task.FromResult(entity);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task<TEntity> LastOrDefaultAsync()
        {
            TEntity entity = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                var last = iterateAsync.Iterate().LastOrDefaultAsync();

                if (last.IsCompleted)
                {
                    entity = last.Result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.LastOrDefaultAsync >>>: {ex}");
            }

            return Task.FromResult(entity);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="storeKey"></param>
        /// <returns></returns>
        public Task<bool> DeleteAsync(StoreKey storeKey)
        {
            bool result = false;

            try
            {
                using var session = _storedbContext.Store.Database.NewSession(new StoreFunctions());
                var deleteStatus = session.Delete(ref storeKey);

                if (deleteStatus != Status.OK)
                    throw new Exception();

                result = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.DeleteAsync >>>: {ex}");
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="take"></param>
        /// <returns></returns>
        public ValueTask<List<TEntity>> TakeAsync(int take)
        {
            ValueTask<List<TEntity>> entities = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                entities = iterateAsync.Iterate().Take(take).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.TakeAsync >>>: {ex}");
            }

            return entities;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public ValueTask<List<TEntity>> RangeAsync(int skip, int take)
        {
            ValueTask<List<TEntity>> entities = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                entities = iterateAsync.Iterate().Skip(skip).Take(take).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.RangeAsync >>>: {ex}");
            }

            return entities;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        public ValueTask<List<TEntity>> TakeLastAsync(int n)
        {
            ValueTask<List<TEntity>> entities = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                entities = iterateAsync.Iterate().TakeLast(n).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.TakeLastAsync >>>: {ex}");
            }

            return entities;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public ValueTask<List<TEntity>> TakeWhileAsync(Func<TEntity, ValueTask<bool>> expression)
        {
            ValueTask<List<TEntity>> entities = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                entities = iterateAsync.Iterate().TakeWhileAwait(expression).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.TakeWhileAsync >>>: {ex}");
            }

            return entities;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="selector"></param>
        /// <returns></returns>
        public ValueTask<List<TEntity>> SelectAsync(Func<TEntity, ValueTask<TEntity>> selector)
        {
            ValueTask<List<TEntity>> entities = default;

            try
            {
                using var iterateAsync = CreateIterateAsync();
                entities = iterateAsync.Iterate().SelectAwait(selector).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.SelectAsync >>>: {ex}");
            }

            return entities;
        }

        protected class IterateAsyncWrapper : IDisposable
        {
            private bool _disposed = false;
            private readonly IStoredbContext _context;
            private string _tableType;

            public IterateAsyncWrapper(IStoredbContext context, string tableType)
            {
                _context = context;
                _tableType = tableType;
            }

            public async IAsyncEnumerable<TEntity> Iterate()
            {
                using var iter = _context.Store.Database.Log.Scan(_context.Store.Database.Log.BeginAddress, _context.Store.Database.Log.TailAddress, ScanBufferingMode.NoBuffering);
                while (iter.GetNext(out _, out StoreKey storeKey, out StoreValue storeValue))
                {
                    if (storeKey.tableType == _tableType)
                    {
                        yield return Helper.Util.DeserializeProto<TEntity>(storeValue.value);
                    }
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (_disposed)
                {
                    return;
                }

                if (disposing)
                {

                }

                _disposed = true;
            }
        }

        protected IterateAsyncWrapper CreateIterateAsync()
        {
            return new(_storedbContext, _tableType);
        }
    }
}