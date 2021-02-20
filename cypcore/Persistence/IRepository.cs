// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CYPCore.Persistence
{
    public interface IRepository<T>
    {
        Task<long> CountAsync();
        Task<T> GetAsync(byte[] key);
        Task<T> FirstAsync(Func<T, ValueTask<bool>> expression);
        void SetTableName(string tableName);
        Task<bool> PutAsync(byte[] key, T data);
        Task<HashSet<T>> RangeAsync(long skip, int take);
        Task<T> LastAsync();
        ValueTask<List<T>> WhereAsync(Func<T, ValueTask<bool>> expression);
        Task<T> LastAsync(Func<T, ValueTask<bool>> expression);
        Task<T> FirstAsync();
        ValueTask<List<T>> SelectAsync(Func<T, ValueTask<T>> selector);
        ValueTask<List<T>> SkipAsync(int skip);
        ValueTask<List<T>> TakeAsync(int take);
        Task<bool> RemoveAsync(byte[] key);
    }
}