// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Net.Http;
using System.Threading.Tasks;

using Refit;

using CYPCore.Models;

namespace CYPCore.Services.Rest
{
    public class RestBlockService
    {
        private readonly HttpClient _httpClient;
        private readonly IRestBlockService _restBlockService;

        public RestBlockService(Uri baseUrl)
        {
            _httpClient = new() { BaseAddress = baseUrl };
            _restBlockService = RestService.For<IRestBlockService>(_httpClient);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<ProtobufStream> GetBlockHeaders(int skip, int take)
        {
            return await _restBlockService.GetBlockHeaders(skip, take).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<BlockHeight> GetHeight()
        {
            return await _restBlockService.GetHeight().ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<WebResponse> AddBlock(byte[] payload)
        {
            return await _restBlockService.AddBlock(payload).ConfigureAwait(false);
        }
    }
}
