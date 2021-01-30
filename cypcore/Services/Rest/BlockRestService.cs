using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

using Refit;

using CYPCore.Models;
using System.Linq;

namespace CYPCore.Services.Rest
{
    public class BlockRestService
    {
        private readonly HttpClient _httpClient;
        private readonly IBlockRestAPI _blockAPI;

        public BlockRestService(Uri baseUrl)
        {
            _httpClient = new() { BaseAddress = baseUrl };
            _blockAPI = RestService.For<IBlockRestAPI>(_httpClient);
        }

        /// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
        public async Task<BlockHeight> Height() => await _blockAPI.Height().ConfigureAwait(false);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<IEnumerable<BlockHeaderProto>> Range(int skip, int take)
        {
            var blockHeaders = Enumerable.Empty<BlockHeaderProto>();

            try
            {
                Block block = await _blockAPI.Range(skip, take).ConfigureAwait(false);
                if (block != null)
                {
                    if (block.Protobufs != null)
                    {
                        blockHeaders = Helper.Util.DeserializeListProto<BlockHeaderProto>(block.Protobufs);
                    }
                }
            }
            catch (Exception)
            {
            }

            return blockHeaders;
        }
    }
}
