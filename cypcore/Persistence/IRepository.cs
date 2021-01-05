// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CYPCore.Persistence
{
    public interface IRepository<TEntity>
    {
        Task<int> CountAsync();
        Task<TEntity> FirstOrDefaultAsync();
        Task<TEntity> FirstOrDefaultAsync(Func<TEntity, bool> expression);
        Task<TEntity> GetAsync(byte[] key);
        Task<IEnumerable<TEntity>> WhereAsync(Func<TEntity, ValueTask<bool>> expression);
        Task<TEntity> LastOrDefaultAsync(Func<TEntity, bool> expression);
        Task<TEntity> LastOrDefaultAsync();
        Task<bool> DeleteAsync(StoreKey storeKey);
        void SetTableType(string table);
        Task<IEnumerable<TEntity>> TakeAsync(int take);
        Task<IEnumerable<TEntity>> TakeWhileAsync(Func<TEntity, ValueTask<bool>> expression);
        Task<IEnumerable<TEntity>> TakeLastAsync(int n);
        Task<IEnumerable<TEntity>> SelectAsync(Func<TEntity, ValueTask<TEntity>> selector);
        Task<TEntity> PutAsync(TEntity entity, byte[] key);
        Task<IEnumerable<TEntity>> RangeAsync(int skip, int take);
    }
}