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
        Task<TEntity> FirstOrDefaultAsync(Func<TEntity, ValueTask<bool>> expression);
        Task<TEntity> GetAsync(byte[] key);
        ValueTask<List<TEntity>> WhereAsync(Func<TEntity, ValueTask<bool>> expression);
        Task<TEntity> LastOrDefaultAsync(Func<TEntity, ValueTask<bool>> expression);
        Task<TEntity> LastOrDefaultAsync();
        Task<bool> DeleteAsync(StoreKey storeKey);
        void SetTableType(string table);
        ValueTask<List<TEntity>> TakeAsync(int take);
        ValueTask<List<TEntity>> TakeWhileAsync(Func<TEntity, ValueTask<bool>> expression);
        ValueTask<List<TEntity>> TakeLastAsync(int n);
        ValueTask<List<TEntity>> SelectAsync(Func<TEntity, ValueTask<TEntity>> selector);
        Task<TEntity> PutAsync(TEntity entity, byte[] key);
        ValueTask<List<TEntity>> RangeAsync(int skip, int take);
    }
}