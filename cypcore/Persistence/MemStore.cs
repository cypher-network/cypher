// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Concurrent;
using System.Collections.Generic;
using CYPCore.Extensions;
using Dawn;
using RocksDbSharp;

namespace CYPCore.Persistence
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    public class MemStore<TItem>
    {
        private readonly ConcurrentDictionary<byte[], TItem> _innerData = new(BinaryComparer.Default);
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IMemSnapshot<TItem> GetMemSnapshot()
        {
            return new MemSnapshot<TItem>(_innerData);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        public void Delete(byte[] key)
        {
            _innerData.Remove(key, out _);
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Put(byte[] key, TItem value)
        {
            Guard.Argument(key, nameof(key)).NotNull().NotEmpty();
            Guard.Argument(value, nameof(value)).HasValue();
            _innerData[key.EnsureNotNull()] = value;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool TryGet(byte[] key, out TItem item)
        {
            Guard.Argument(key, nameof(key)).NotNull().NotEmpty();
            if (_innerData.TryGetValue(key.EnsureNotNull(), out var cacheItem))
            {
                item = cacheItem;
                return true;
            }

            item = default;
            return false;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool Contains(byte[] key)
        {
            Guard.Argument(key, nameof(key)).NotNull().NotEmpty();
            return _innerData.TryGetValue(key, out _);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            return _innerData.Count;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Clear()
        {
            _innerData.Clear();
        }
    }
}