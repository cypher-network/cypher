// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Models;
using CYPCore.Extentions;

namespace CYPCore.Persistence
{
    public class MemPoolRepository : Repository<MemPoolProto>, IMemPoolRepository
    {
        private const string TableMemPool = "MemPool";

        private readonly IStoredbContext _storedbContext;
        private readonly ILogger _logger;

        public string Table => TableMemPool;

        public MemPoolRepository(IStoredbContext storedbContext, ILogger logger)
            : base(storedbContext, logger)
        {
            _storedbContext = storedbContext;
            _logger = logger;

            SetTableType(TableMemPool);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="round"></param>
        /// <returns></returns>
        public Task<MemPoolProto> PreviousOrDefaultAsync(ulong node, ulong round)
        {
            Guard.Argument(node, nameof(node)).NotNegative();
            Guard.Argument(round, nameof(round)).NotNegative();

            MemPoolProto block = default;

            try
            {
                round -= 1;
                block = FirstOrDefaultAsync(x => x.Block.Node == node && x.Block.Round == round).Result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockGraphRepository.PreviousOrDefaultAsync >>>: {ex}");
            }

            return Task.FromResult(block);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="node"></param>
        /// <param name="round"></param>
        /// <returns></returns>
        public Task<MemPoolProto> PreviousOrDefaultAsync(byte[] hash, ulong node, ulong round)
        {
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(48);
            Guard.Argument(node, nameof(node)).NotNegative();
            Guard.Argument(round, nameof(round)).NotNegative();

            MemPoolProto block = default;

            try
            {
                round -= 1;
                block = FirstOrDefaultAsync(x => x.Block.Hash.Equals(hash) && x.Block.Node == node && x.Block.Round == round).Result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockGraphRepository.PreviousOrDefaultAsync >>>: {ex}");
            }

            return Task.FromResult(block);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPools"></param>
        /// <returns></returns>
        public async Task<List<MemPoolProto>> MoreAsync(IEnumerable<MemPoolProto> memPools)
        {
            Guard.Argument(memPools, nameof(memPools)).NotNull().NotEmpty();

            var moreBlocks = new List<MemPoolProto>();

            try
            {
                foreach (var next in memPools)
                {
                    var hasNext = await WhereAsync(x => new ValueTask<bool>(x.Block.Hash.Equals(next.Block.Hash)));

                    IEnumerable<(MemPoolProto nNext, MemPoolProto included)> enumerable()
                    {
                        foreach (var nNext in hasNext)
                        {
                            var included = moreBlocks
                                .FirstOrDefault(x => x.Block.Hash.Equals(nNext.Block.Hash)
                                    && !string.IsNullOrEmpty(x.Block.PublicKey)
                                    && !string.IsNullOrEmpty(x.Block.Signature)
                                    && x.Block.Node == nNext.Block.Node
                                    && x.Block.Round == nNext.Block.Round);

                            yield return (nNext, included);
                        }
                    }

                    foreach (var (nNext, included) in enumerable())
                    {
                        if (included != null)
                            continue;

                        moreBlocks.Add(nNext);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockGraphRepository.MoreAsync >>>: {ex}");
            }

            return moreBlocks;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPools"></param>
        /// <param name="currentNode"></param>
        /// <returns></returns>
        public async Task IncludeAllAsync(IEnumerable<MemPoolProto> memPools, ulong currentNode)
        {
            Guard.Argument(memPools, nameof(memPools)).NotNull().NotEmpty();
            Guard.Argument(currentNode, nameof(currentNode)).NotNegative();

            try
            {
                foreach (var next in memPools.Where(x => x.Block.Node == currentNode))
                {
                    next.Included = true;
                    await PutAsync(next, next.ToIdentifier());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockGraphRepository.IncludeAllAsync >>>: {ex}");
            }

            return;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="node"></param>
        /// <param name="round"></param>
        /// <returns></returns>
        public Task<MemPoolProto> Get(byte[] hash, ulong node, ulong round)
        {
            MemPoolProto memPool = default;

            try
            {
                 memPool = FirstOrDefaultAsync(x => x.Block.Hash.Equals(hash.ByteToHex()) && x.Block.Node == node && x.Block.Round == round).Result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockGraphRepository.Get >>>: {ex}");
            }

            return Task.FromResult(memPool);
        }
    }
}
