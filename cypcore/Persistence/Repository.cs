// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq.Expressions;

using Microsoft.Extensions.Logging;

namespace CYPCore.Persistence
{
    public class Repository<TEntity> : IRepository<TEntity> where TEntity : new()
    {
        private readonly IStoredb _storedb;
        private readonly ILogger _logger;

        public Repository() { }

        public Repository(IStoredb storedb, ILogger logger)
        {
            _storedb = storedb;
            _logger = logger;
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
                count = _storedb.RockDb.Table<TEntity>().Count();
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
        public Task<List<TEntity>> WhereAsync(Expression<Func<TEntity, bool>> expression)
        {
            List<TEntity> entities = default;

            try
            {
                entities = _storedb.RockDb.Table<TEntity>().Where(expression).SelectEntity();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.WhereAsync >>>: {ex}");
            }

            return Task.FromResult(entities);
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
                entity = _storedb.RockDb.Table<TEntity>().SelectEntity().FirstOrDefault();
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
        public Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> expression)
        {
            TEntity entity = default;

            try
            {
                entity = _storedb.RockDb.Table<TEntity>().Where(expression).SelectEntity().FirstOrDefault();
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
        public Task<TEntity> LastOrDefaultAsync(Expression<Func<TEntity, bool>> expression)
        {
            TEntity entity = default;

            try
            {
                entity = _storedb.RockDb.Table<TEntity>().Where(expression).SelectEntity().LastOrDefault();
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
                entity = _storedb.RockDb.Table<TEntity>().SelectEntity().LastOrDefault();
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
        /// <param name="take"></param>
        /// <returns></returns>
        public Task<List<TEntity>> TakeAsync(int take)
        {
            List<TEntity> entities = default;

            try
            {
                entities = _storedb.RockDb.Table<TEntity>().SelectEntity().Take(take).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.TakeAsync >>>: {ex}");
            }

            return Task.FromResult(entities);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <returns></returns>
        public Task<List<TEntity>> SkipAsync(int skip)
        {
            List<TEntity> entities = default;

            try
            {
                entities = _storedb.RockDb.Table<TEntity>().SelectEntity().Skip(skip).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.SkipAsync >>>: {ex}");
            }

            return Task.FromResult(entities);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public Task<List<TEntity>> RangeAsync(int skip, int take)
        {
            List<TEntity> entities = default;

            try
            {

                entities = _storedb.RockDb.Table<TEntity>().SelectEntity().Skip(skip).Take(take).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.RangeAsync >>>: {ex}");
            }

            return Task.FromResult(entities);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="selector"></param>
        /// <returns></returns>
        public Task<List<TEntity>> SelectAsync(Expression<Func<TEntity, TEntity>> selector)
        {
            List<TEntity> entities = default;

            try
            {
                entities = _storedb.RockDb.Table<TEntity>().Select(selector);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.SelectAsync >>>: {ex}");
            }

            return Task.FromResult(entities);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public Task<int?> SaveOrUpdateAsync(TEntity entity)
        {
            int? id = null;

            try
            {
                id = _storedb.RockDb.Table<TEntity>().Save(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.SaveOrUpdateAsync >>>: {ex}");
            }

            return Task.FromResult(id);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Task<bool> DeleteAsync(int id)
        {
            bool deleted = false;
            try
            {
                _storedb.RockDb.Table<TEntity>().Delete(new HashSet<int>() { id });
                deleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Repository.DeleteAsync >>>: {ex}");
            }

            return Task.FromResult(deleted);
        }
    }
}