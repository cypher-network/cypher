// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Net.Http;
using System.Threading.Tasks;
using CYPCore.Models;
using Refit;

namespace CYPCore.Services.Rest
{
    /// <summary>
    /// 
    /// </summary>
    public interface ITransactionRestService
    {
        [Post("/transaction")]
        Task<WebResponse> AddTransaction(byte[] tx);
    }

    /// <summary>
    /// 
    /// </summary>
    public class TransactionRestService
    {
        private readonly ITransactionRestService _restTransactionService;

        public TransactionRestService(Uri baseUrl)
        {
            HttpClient httpClient = new() { BaseAddress = baseUrl };
            _restTransactionService = RestService.For<ITransactionRestService>(httpClient);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task<WebResponse> AddTransaction(byte[] tx)
        {
            return await _restTransactionService.AddTransaction(tx).ConfigureAwait(false);
        }
    }
}