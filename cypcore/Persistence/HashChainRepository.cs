// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
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
    public interface IHashChainRepository : IRepository<Block>
    {
        ValueTask<List<Block>> OrderByRangeAsync(Func<Block, ulong> selector, int skip, int take);
        new Task<bool> PutAsync(byte[] key, Block data);
    }

    public class HashChainRepository : Repository<Block>, IHashChainRepository
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;
        private readonly ReaderWriterLockSlim _sync = new();

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
        public new Task<bool> PutAsync(byte[] key, Block data)
        {
            Guard.Argument(key, nameof(key)).NotNull();
            Guard.Argument(data, nameof(data)).NotNull();

            if (data.Validate().Any())
            {
                return Task.FromResult(false);
            }

            var saved = false;
            try
            {
                using (_sync.Write())
                {
                    var cf = _storeDb.Rocks.GetColumnFamily(StoreDb.HashChainTable.ToString());
                    var buffer = MessagePackSerializer.Serialize(data);

                    _storeDb.Rocks.Put(StoreDb.Key(StoreDb.HashChainTable.ToString(), key), buffer, cf);

                    saved = true;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while storing in database");
            }

            return Task.FromResult(saved);
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
            ValueTask<List<Block>> entries = default;

            try
            {
                using (_sync.Read())
                {
                    entries = Iterate().OrderBy(selector).Skip(skip).Take(take).ToListAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while reading database");
            }

            return entries;
        }
    }
}