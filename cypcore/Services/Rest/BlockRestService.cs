// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

using Refit;

using CYPCore.Models;

namespace CYPCore.Services.Rest
{
    public class RestBlockService : IBlockService
    {
        private readonly HttpClient _httpClient;
        private readonly IBlockService _blockRestAPI;

        public RestBlockService(Uri baseUrl)
        {
            _httpClient = new() { BaseAddress = baseUrl };
            _blockRestAPI = RestService.For<IBlockService>(_httpClient);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<bool> AddBlock(byte[] payload)
        {
            return await _blockRestAPI.AddBlock(payload);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payloads"></param>
        /// <returns></returns>
        public async Task AddBlocks(byte[] payloads)
        {
            await _blockRestAPI.AddBlocks(payloads).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<IEnumerable<BlockHeaderProto>> GetBlockHeaders(int skip, int take)
        {
            return await _blockRestAPI.GetBlockHeaders(skip, take).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<long> GetHeight()
        {
            return await _blockRestAPI.GetHeight().ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<BlockHeaderProto>> GetSafeguardBlocks()
        {
            return await _blockRestAPI.GetSafeguardBlocks().ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="txnId"></param>
        /// <returns></returns>
        public async Task<byte[]> GetVout(byte[] txnId)
        {
            return await _blockRestAPI.GetVout(txnId).ConfigureAwait(false);
        }
    }
}
