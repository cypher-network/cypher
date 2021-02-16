// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Models;

namespace CYPCore.Persistence
{
    public class MemPoolRepository : Repository<MemPoolProto>, IMemPoolRepository
    {
        private readonly IStoredb _storedb;
        private readonly ILogger _logger;

        public MemPoolRepository(IStoredb storedbContext, ILogger logger)
            : base(storedbContext, logger)
        {
            _storedb = storedbContext;
            _logger = logger;
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
                    var hasNext = await WhereAsync(x => x.Block.Hash.Equals(next.Block.Hash));

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
                _logger.LogError($"<<< MemPoolRepository.MoreAsync >>>: {ex}");
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
                    next.Included = 1;
                    await SaveOrUpdateAsync(next);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MemPoolRepository.IncludeAllAsync >>>: {ex}");
            }

            return;
        }
    }
}
