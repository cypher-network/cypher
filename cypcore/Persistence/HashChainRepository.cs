// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Models;
using Dawn;
using MessagePack;
using Serilog;

namespace CYPCore.Persistence
{
    /// <summary>
    /// 
    /// </summary>
    public interface IHashChainRepository : IRepository<Block>
    {
        ValueTask<List<Block>> OrderByRangeAsync(Func<Block, ulong> selector, int skip, int take);
        new Task<bool> PutAsync(byte[] key, Block data);
    }

    /// <summary>
    /// 
    /// </summary>
    public class HashChainRepository : Repository<Block>, IHashChainRepository
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;
        private readonly ReaderWriterLockSlim _sync = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="storeDb"></param>
        /// <param name="logger"></param>
        public HashChainRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            _storeDb = storeDb;
            _logger = logger.ForContext("SourceContext", nameof(HashChainRepository));

            SetTableName(StoreDb.HashChainTable.ToString());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public new async Task<bool> PutAsync(byte[] key, Block data)
        {
            Guard.Argument(key, nameof(key)).NotNull().MaxCount(64);
            Guard.Argument(data, nameof(data)).NotNull();
            if (data.Validate().Any())
            {
                return false;
            }

            try
            {
                using (_sync.Write())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(StoreDb.HashChainTable.ToString());
                    _storeDb.Rocks.Put(StoreDb.Key(StoreDb.HashChainTable.ToString(), key),
                        await Helper.Util.SerializeAsync(data), cf);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while storing in database");
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="selector"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public ValueTask<List<Block>> OrderByRangeAsync(Func<Block, ulong> selector, int skip, int take)
        {
            Guard.Argument(selector, nameof(selector)).NotNull();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();
            try
            {
                using (_sync.Read())
                {
                    var entries = Iterate().OrderBy(selector).Skip(skip).Take(take).ToListAsync();
                    return entries;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return default;
        }
    }
}