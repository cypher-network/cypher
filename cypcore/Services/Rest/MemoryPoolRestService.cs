// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Net.Http;
using System.Threading.Tasks;

using Refit;

using CYPCore.Models;

namespace CYPCore.Services.Rest
{
    public class MemoryPoolRestService : IMemoryPoolService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryPoolService _memoryPoolRestApi;

        public MemoryPoolRestService(Uri baseUrl)
        {
            _httpClient = new() { BaseAddress = baseUrl };
            _memoryPoolRestApi = RestService.For<IMemoryPoolService>(_httpClient);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pool"></param>
        /// <returns></returns>
        public async Task<bool> AddMemoryPool(byte[] pool)
        {
            return await _memoryPoolRestApi.AddMemoryPool(pool).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pools"></param>
        /// <returns></returns>
        public async Task AddMemoryPools(byte[] pools)
        {
            await _memoryPoolRestApi.AddMemoryPools(pools).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task<byte[]> AddTransaction(TransactionProto tx)
        {
            return await _memoryPoolRestApi.AddTransaction(tx).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetTransactionCount()
        {
            return await _memoryPoolRestApi.GetTransactionCount().ConfigureAwait(false);
        }
    }
}
