// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Models;
using Serilog;

namespace CYPCore.Persistence
{
    public interface IHashChainRepository : IRepository<BlockHeader>
    {
        ValueTask<List<BlockHeader>> OrderByRangeAsync(Func<BlockHeader, long> selector, int skip, int take);
    }

    public class HashChainRepository : Repository<BlockHeader>, IHashChainRepository
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
        /// <param name="selector"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public ValueTask<List<BlockHeader>> OrderByRangeAsync(Func<BlockHeader, long> selector, int skip, int take)
        {
            ValueTask<List<BlockHeader>> entries = default;

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