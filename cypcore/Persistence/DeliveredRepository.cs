// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

using CYPCore.Extensions;
using Serilog;
using Stratis.Patricia;

using CYPCore.Extentions;
using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class DeliveredRepository : Repository<BlockHeaderProto>, IDeliveredRepository
    {
        private readonly IStoreDb _storeDb;
        private readonly ILogger _logger;
        private readonly IPatriciaTrie _stateTrie;

        public DeliveredRepository(IStoreDb storeDb, ILogger logger)
            : base(storeDb, logger)
        {
            _storeDb = storeDb;
            _logger = logger;

            _stateTrie = new PatriciaTrie();

            SetTableName(StoreDb.DeliveredTable.ToString());
        }

        /// <summary>
        /// 
        /// </summary>
        public byte[] MerkleRoot => _stateTrie.GetRootHash();

        /// <summary>
        /// 
        /// </summary>
        public void CommitTrie()
        {
            _stateTrie.Flush();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        public BlockHeaderProto ToTrie(BlockHeaderProto blockHeader)
        {
            try
            {
                _stateTrie.Put(blockHeader.ToHash(), blockHeader.ToHash());
                _stateTrie.Flush();

                blockHeader.MrklRoot = MerkleRoot.ByteToHex();
            }
            catch (System.Exception ex)
            {
                blockHeader = null;
                _logger.Here().Error(ex, "Error while adding to trie");
            }

            return blockHeader;
        }

        /// <summary>
        /// 
        /// </summary>
        public void ResetTrie()
        {
            _stateTrie.SetRootHash(Array.Empty<byte>());
        }
    }
}