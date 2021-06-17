using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Persistence;
using Dawn;
using MessagePack;
using Newtonsoft.Json.Linq;
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
                var httpResponseMessage = await _httpClient.GetAsync($"{peer.Host}/chain/height");
                httpResponseMessage.EnsureSuccessStatusCode();
                var content = await httpResponseMessage.Content.ReadAsStringAsync();
                var blockHeight = Newtonsoft.Json.JsonConvert.DeserializeObject<BlockHeight>(content);
                var networkBlockHeight = new NetworkBlockHeight
                {
                    Local = new BlockHeight
                    { Height = (ulong)await _unitOfWork.HashChainRepository.CountAsync(), Host = "local" },
                    Remote = new BlockHeight { Height = blockHeight.Height == 0 ? 0 : blockHeight.Height - 1, Host = peer.Host }
                };

                var remoteBlock = await GetBlocksAsync(peer.Host, networkBlockHeight.Remote.Height, 1);

                // block height 0 retrieves the last block hash (highest height)
                //httpResponseMessage = await _httpClient.GetAsync($"{peer.Host}/chain/blocks/{networkBlockHeight.Remote.Height}/1");
                //httpResponseMessage.EnsureSuccessStatusCode();
                //content = await httpResponseMessage.Content.ReadAsStringAsync();

                return new()
                {
                    Peer = peer,
                    BlockHash = new()
                    {
                        Hash = remoteBlock.Last().ToHash(),
                        Height = (ulong)networkBlockHeight.Remote.Height
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
        public async Task<IList<Block>> GetBlocksAsync(string host, ulong skip, int take)
        {
            IList<Block> blocks = null;
            try
            {
                var httpResponseMessage = await _httpClient.GetAsync($"{host}/chain/blocks/{skip}/{take}");
                httpResponseMessage.EnsureSuccessStatusCode();
                var content = await httpResponseMessage.Content.ReadAsStringAsync();
                var jObject = JObject.Parse(content);
                var jToken = jObject.GetValue("messagepack");
                var byteArray =
                    Convert.FromBase64String((jToken ?? throw new InvalidOperationException()).Value<string>());
                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    var genericList = MessagePackSerializer.Deserialize<GenericDataList<Block>>(byteArray);
                    blocks = genericList.Data;
                }
                else
                {
                    content = await httpResponseMessage.Content.ReadAsStringAsync();
                    _logger.Here().Error("{@Content}\n StatusCode: {@StatusCode}", content,
                        (int)httpResponseMessage.StatusCode);
                    throw new Exception(content);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to deserialize object for {@host}", host);
            }

            return blocks;
        }
    }
}