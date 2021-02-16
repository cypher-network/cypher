// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace CYPCore.Persistence
{
    public interface IRepository<TEntity>
    {
        Task<int> CountAsync();
        Task<TEntity> FirstOrDefaultAsync();
        Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> expression);
        Task<List<TEntity>> WhereAsync(Expression<Func<TEntity, bool>> expression);
        Task<TEntity> LastOrDefaultAsync(Expression<Func<TEntity, bool>> expression);
        Task<TEntity> LastOrDefaultAsync();
        Task<bool> DeleteAsync(int id);
        Task<List<TEntity>> SkipAsync(int skip);
        Task<List<TEntity>> TakeAsync(int take);
        Task<List<TEntity>> SelectAsync(Expression<Func<TEntity, TEntity>> selector);
        Task<List<TEntity>> RangeAsync(int skip, int take);
        Task<int?> SaveOrUpdateAsync(TEntity entity);
    }
}