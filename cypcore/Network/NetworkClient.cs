using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Persistence;
using Dawn;
using FlatSharp;
using Serilog;

namespace CYPCore.Network
{
    public class NetworkClient
    {
        private readonly SemaphoreSlim _semaphore;

        private readonly HttpClient _httpClient;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _logger;

        public NetworkClient(HttpClient httpClient, IUnitOfWork unitOfWork, ILogger logger)
        {
            _httpClient = httpClient;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _semaphore = new SemaphoreSlim(6);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="peer"></param>
        /// <returns></returns>
        public async Task<BlockHashPeer> GetPeerLastBlockHashAsync(Peer peer)
        {
            try
            {
                // block height 0 retrieves the last block hash (highest height)
                var httpResponseMessage = await _httpClient.GetAsync($"{peer.Host}/chain/hash/0");
                httpResponseMessage.EnsureSuccessStatusCode();
                var content = await httpResponseMessage.Content.ReadAsStringAsync();
                return new()
                {
                    Peer = peer,
                    BlockHash =
                    {
                        Hash = Newtonsoft.Json.JsonConvert.DeserializeObject<byte[]>(content)
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error getting last block hash");
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="topicType"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        public async Task SendAsync(byte[] data, TopicType topicType, string host)
        {
            Guard.Argument(data, nameof(data)).NotNull();
            Guard.Argument(host, nameof(data)).NotNull().NotEmpty().NotWhiteSpace();
            try
            {
                if (Uri.TryCreate($"{host}", UriKind.Absolute, out var uri))
                {
                    await _semaphore.WaitAsync();
                    if (topicType == TopicType.AddBlockGraph)
                    {
                        var postResponse = await _httpClient.PostAsJsonAsync($"{host}/chain/blockgraph", data);
                        postResponse.EnsureSuccessStatusCode();
                    }
                    else if (topicType == TopicType.AddTransaction)
                    {
                        var postResponse = await _httpClient.PostAsJsonAsync($"{host}/mem/transaction", data);
                        postResponse.EnsureSuccessStatusCode();
                    }
                }
                else
                {
                    _logger.Here().Error("Cannot create URI for host {@Host}", host);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to send for {@host}", host);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="host"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<IList<BlockHeaderProto>> GetBlocksAsync(string host, long skip, int take)
        {
            IList<BlockHeaderProto> blockHeaders = null;
            try
            {
                var httpResponseMessage = await _httpClient.GetAsync($"{host}/chain/blocks/{(int)skip}/{take}");
                httpResponseMessage.EnsureSuccessStatusCode();
                var content = await httpResponseMessage.Content.ReadAsStringAsync();
                var flatBufferStream = Newtonsoft.Json.JsonConvert.DeserializeObject<FlatBufferStream>(content);
                var genericList = FlatBufferSerializer.Default.Parse<GenericList<BlockHeaderProto>>(flatBufferStream.FlatBuffer);

                blockHeaders = genericList.Data;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            return blockHeaders;
        }
    }
}