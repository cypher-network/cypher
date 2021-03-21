// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Net.Http;
using System.Threading.Tasks;
using CYPCore.Models;
using Refit;
using Serilog;

namespace CYPCore.Services.Rest
{
    /// <summary>
    /// 
    /// </summary>
    public interface IBlockGraphRestService
    {
        [Post("/header/blockgraph")]
        Task<WebResponse> AddBlockGraph(byte[] payload);
    }

    /// <summary>
    /// 
    /// </summary>
    public class BlockGraphRestService
    {
        private readonly IBlockGraphRestService _blockGraphRestService;

        public BlockGraphRestService(Uri baseUrl, ILogger logger)
        {
            logger = logger.ForContext("SourceContext", nameof(BlockGraphRestService));
            HttpClient httpClient = new(new RestLoggingHandler(logger)) { BaseAddress = baseUrl };
            _blockGraphRestService = RestService.For<IBlockGraphRestService>(httpClient);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<WebResponse> AddBlockGraph(byte[] payload)
        {
            return await _blockGraphRestService.AddBlockGraph(payload).ConfigureAwait(false);
        }
    }
}