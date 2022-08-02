// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using RocksDbSharp;

namespace CypherNetwork.Persistence;

public class Caching<TItem> 
{
    private readonly Dictionary<byte[], TItem> _innerDictionary = new(BinaryComparer.Default);
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.SupportsRecursion);
    
    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    public TItem this[byte[] key]
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            {
                return _innerDictionary[key];
            }
            catch (Exception)
            {
                return default;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// </summary>
    public int Count
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            {
                return _innerDictionary.Count;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }

        }
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <param name="item"></param>
    public void Add(byte[] key, TItem item)
    {
        _rwLock.EnterWriteLock();
        try
        {
            if (!_innerDictionary.TryGetValue(key, out _)) _innerDictionary.Add(key, item);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <param name="item"></param>
    public bool AddOrUpdate(byte[] key, TItem item)
    {
        _rwLock.EnterWriteLock();
        try
        {
            if (_innerDictionary.TryGetValue(key, out _))
            {
                _innerDictionary[key] = item;
                return true;
            }
            else
            {
                _innerDictionary.Add(key, item);
                return true;
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    public bool Remove(byte[] key)
    {
        _rwLock.EnterWriteLock();
        try
        {
            if (_innerDictionary.TryGetValue(key, out var cachedItem))
            {
                _innerDictionary.Remove(key);
                if (cachedItem is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                return true;
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        return false;
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <param name="item"></param>
    /// <returns></returns>
    public bool TryGet(byte[] key, out TItem item)
    {
        _rwLock.EnterReadLock();
        try
        {
            if (_innerDictionary.TryGetValue(key, out var cacheItem))
            {
                item = cacheItem;
                return true;
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        item = default;
        return false;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public TItem[] GetItems()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _innerDictionary.Values.ToArray();
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// </summary>
    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        {
            foreach (var (key, _) in _innerDictionary) Remove(key);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public bool Contains(byte[] key)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _innerDictionary.TryGetValue(key, out _);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    public ValueTask<KeyValuePair<byte[], TItem>[]> WhereAsync(
        Func<KeyValuePair<byte[], TItem>, ValueTask<bool>> expression)
    {
        Guard.Argument(expression, nameof(expression)).NotNull();
        _rwLock.EnterReadLock();
        try
        {
            var entries = IterateAsync().WhereAwait(expression).ToArrayAsync();
            return entries;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    public IEnumerable<KeyValuePair<byte[], TItem>> Where(Func<KeyValuePair<byte[], TItem>, bool> expression)
    {
        Guard.Argument(expression, nameof(expression)).NotNull();

        _rwLock.EnterReadLock();
        try
        {
            var entries = IterateAsync().Where(expression).ToEnumerable();
            return entries;
        }
        finally
        {
            _rwLock.ExitReadLock();   
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private IAsyncEnumerable<KeyValuePair<byte[], TItem>> IterateAsync()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _innerDictionary.ToAsyncEnumerable();
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }
}