// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.Extensions.Logging;

using Stratis.Patricia;
using CYPCore.Extentions;
using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class DeliveredRepository : Repository<BlockHeaderProto>, IDeliveredRepository
    {
        private readonly IStoredb _storedb;
        private readonly ILogger _logger;
        private readonly IPatriciaTrie _stateTrie;

        public DeliveredRepository(IStoredb storedb, ILogger logger)
            : base(storedb, logger)
        {
            _storedb = storedb;
            _logger = logger;

            _stateTrie = new PatriciaTrie();

            //InitTrie();
        }

        /// <summary>
        /// 
        /// </summary>
        //private void InitTrie()
        //{
        //    try
        //    {
        //        var blockHeader = LastOrDefaultAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        //        if (blockHeader == null)
        //            return;

        //        _stateTrie.SetRootHash(blockHeader.MrklRoot.FromHex());
        //    }
        //    catch (System.Exception ex)
        //    {
        //        _logger.LogError($"<<< DeliveredRepository.InitTrie >>>: {ex}");
        //    }
        //}

        /// <summary>
        /// 
        /// </summary>
        public byte[] MrklRoot => _stateTrie.GetRootHash();

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

                blockHeader.MrklRoot = MrklRoot.ByteToHex();
            }
            catch (System.Exception ex)
            {
                blockHeader = null;
                _logger.LogError($"<<< DeliveredRepository.AddToTrie >>>: {ex}");
            }

            return blockHeader;
        }

        /// <summary>
        /// 
        /// </summary>
        public void ResetTrie()
        {
            _stateTrie.SetRootHash(new byte[0]);
        }
    }
}
