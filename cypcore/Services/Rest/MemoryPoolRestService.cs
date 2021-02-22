// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Net.Http;
using System.Threading.Tasks;

using Refit;

using CYPCore.Models;

namespace CYPCore.Services.Rest
{
    /// <summary>
    /// 
    /// </summary>
    public interface IRestMemoryPoolService
    {
        [Post("/pool")]
        Task<WebResponse> AddMemoryPool(byte[] pool);
    }

    /// <summary>
    /// 
    /// </summary>
    public class RestMemoryPoolService
    {
        private readonly HttpClient _httpClient;
        private readonly IRestMemoryPoolService _restMemoryPoolService;

        public RestMemoryPoolService(Uri baseUrl)
        {
            _httpClient = new() { BaseAddress = baseUrl };
            _restMemoryPoolService = RestService.For<IRestMemoryPoolService>(_httpClient);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pool"></param>
        /// <returns></returns>
        public async Task<WebResponse> AddMemoryPool(byte[] pool)
        {
            return await _restMemoryPoolService.AddMemoryPool(pool).ConfigureAwait(false);
        }
    }
}
