// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using CYPCore.Models;
using Serilog;
using Stratis.Patricia;

namespace CYPCore.Persistence
{
    public interface ITrieRepository : IRepository<TrieModel>, ISource<byte[], byte[]>
    {
    }

    public class TrieRepository : Repository<TrieModel>, ITrieRepository
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;

        public TrieRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            _storeDb = storeDb;
            _logger = logger.ForContext("SourceContext", nameof(TrieRepository));

            SetTableName(StoreDb.TrieTable.ToString());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="val"></param>
        public void Put(byte[] key, byte[] val)
        {
            try
            {
                var saved = PutAsync(key, new TrieModel { Key = key, Value = val }).GetAwaiter().GetResult();
                if (saved == false) throw new Exception("Unable to save trie item");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public byte[] Get(byte[] key)
        {
            byte[] val = null;

            try
            {
                var model = GetAsync(key).GetAwaiter().GetResult();
                if (model != null)
                {
                    val = model.Value;
                }
            }
            catch (Exception)
            {
                throw;
            }

            return val;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        public void Delete(byte[] key)
        {
            try
            {
                var removed = RemoveAsync(key).GetAwaiter().GetResult();
                if (removed == false) throw new Exception("Unable to remove trie item");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool Flush()
        {
            throw new System.NotImplementedException();
        }
    }
}