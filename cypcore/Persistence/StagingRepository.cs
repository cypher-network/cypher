// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Models;
using CYPCore.Extentions;


namespace CYPCore.Persistence
{
    public class StagingRepository : Repository<StagingProto>, IStagingRepository
    {
        private const string TableStaging = "Staging";

        public string Table => TableStaging;

        private readonly IStoredbContext _storedbContext;
        private readonly ILogger _logger;

        public StagingRepository(IStoredbContext storedbContext, ILogger logger)
            : base(storedbContext, logger)
        {
            _storedbContext = storedbContext;
            _logger = logger;

            SetTableType(TableStaging);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hashes"></param>
        /// <returns></returns>
        public async Task SaveOrUpdateAllCompletedAsync(IEnumerable<byte[]> hashes)
        {
            Guard.Argument(hashes, nameof(hashes)).NotNull().NotEmpty();

            try
            {
                foreach (var hash in hashes)
                {
                    if (hash.Length != 32)
                        throw new IndexOutOfRangeException("Hash length must be 32 bytes in size.");

                    var staging = await FirstOrDefaultAsync(x => x.Hash == hash.ByteToHex());
                    if (staging != null)
                    {
                        staging.Status = StagingState.Delivered;

                        var saved = await PutAsync(staging, staging.ToIdentifier());
                        if (saved == null)
                            throw new Exception($"Could not update staging {staging.Hash}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< StagingRepository.SaveOrUpdateAllCompletedAsync >>>: {ex}");
            }
        }
    }
}
