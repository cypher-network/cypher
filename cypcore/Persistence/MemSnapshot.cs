// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CYPCore.Extensions;
using Dawn;
using RocksDbSharp;

namespace CYPCore.Persistence
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    public interface IMemSnapshot<TItem>
    {
        /// <summary>
        /// 
        /// </summary>
        void Commit();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IAsyncEnumerable<(byte[] key, TItem Value)> SnapshotAsync();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        void Delete(byte[] key);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        void Put(byte[] key, TItem value);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="item"></param>
        /// <returns></returns>k
        bool TryGet(byte[] key, out TItem item);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        bool Contains(byte[] key);

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        int Count();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    public class MemSnapshot<TItem> : IMemSnapshot<TItem>
    {
        private readonly ConcurrentDictionary<byte[], TItem> _innerData = new(BinaryComparer.Default);
        private readonly ImmutableDictionary<byte[], TItem> _immutableData;
        private readonly ConcurrentDictionary<byte[], TItem> _writeBatch = new(BinaryComparer.Default);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="innerData"></param>
        public MemSnapshot(ConcurrentDictionary<byte[], TItem> innerData )
        {
            _immutableData = innerData.ToImmutableDictionary(BinaryComparer.Default);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Commit()
        {
            foreach (var (key, item) in _writeBatch)
                if (item is null) _innerData.TryRemove(key, out _);
                else _innerData[key] = item;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async IAsyncEnumerable<(byte[] key, TItem Value)> SnapshotAsync()
        {
            for (var i = 0; i < _immutableData.Count; i++)
            {
                yield return new ValueTuple<byte[], TItem>(_immutableData.ElementAt(i).Key,
                    _immutableData.ElementAt(i).Value);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        public void Delete(byte[] key)
        {
            _writeBatch[key.EnsureNotNull()] = default;
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
            _writeBatch[key.EnsureNotNull()] = value;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="item"></param>
        /// <returns></returns>k
        public bool TryGet(byte[] key, out TItem item)
        {
            Guard.Argument(key, nameof(key)).NotNull().NotEmpty();
            if (_immutableData.TryGetValue(key.EnsureNotNull(), out var cacheItem))
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
            return _immutableData.Count;;
        }
        
    }
}