// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Autofac;

using WebSocketSharp;
using WebSocketSharp.Server;

using CYPCore.Models;
using CYPCore.Serf;
using CYPCore.Ledger;

namespace CYPCore.Network.P2P
{
    public class MempoolSocketService : WebSocketBehavior, IStartable, IDisposable
    {
        private readonly IMempool _memPool;
        private readonly ISerfClient _serfClient;
        private readonly ILogger _logger;

        private WebSocketServer _wss;
        private CancellationTokenSource _cancel;

        private static MempoolSocketService _instance;

        public MempoolSocketService()
        {
            _logger = NullLogger<MempoolSocketService>.Instance;
        }

        public MempoolSocketService(IMempool memPool, ISerfClient serfClient, ILogger<MempoolSocketService> logger)
        {
            _memPool = memPool;
            _serfClient = serfClient;
            _logger = logger;
            _serfClient.GetClientID().GetAwaiter();

            if (_instance == null)
            {
                _instance = this;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static MempoolSocketService GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start()
        {
            if (GetInstance() == null)
            {
                throw new Exception("Null reference exception on GetInstance()");
            }

            var endpoint = Helper.Util.TryParseAddress(GetInstance()._serfClient.P2PConnectionOptions.TcpServerMempool);

            GetInstance()._wss = new WebSocketServer($"ws://{endpoint.Address}:{endpoint.Port}");
            GetInstance()._wss.AddWebSocketService<MempoolSocketService>($"/{SocketTopicType.Mempool}");
            GetInstance()._wss.Start();

            if (!_wss.IsListening)
            {
                GetInstance()._logger.LogError($"<<< MempoolSocketService.Start >>>: Faild to started P2P socket mempool at ws://{endpoint.Address}:{endpoint.Port}");
            }
            else
            {
                GetInstance()._logger.LogInformation($"<<< MempoolSocketService.Start >>>: Started P2P socket mempool at ws://{endpoint.Address}:{endpoint.Port}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected async override void OnMessage(MessageEventArgs e)
        {
            try
            {
                _cancel = new CancellationTokenSource();

                await Task.Run(() =>
                {
                    var memPools = Enumerable.Empty<MemPoolProto>();

                    try
                    {
                        memPools = Helper.Util.DeserializeListProto<MemPoolProto>(e.RawData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"<<< MempoolSocketService.OnMessage >>>: Could not deserialize payload {ex.Message}");
                    }

                    return memPools;

                }, _cancel.Token).ContinueWith(async mempools =>
                {
                    if (mempools.IsCanceled || mempools.IsFaulted)
                    {
                        throw new Exception(mempools.Exception.Message);
                    }

                    if (mempools.Result?.Any() == true)
                    {
                        foreach (var mempool in mempools.Result)
                        {
                            if (mempool != null)
                            {
                                if (GetInstance() == null)
                                {
                                    break;
                                }

                                if (GetInstance()._serfClient.P2PConnectionOptions.ClientId == mempool.Block.Node)
                                {
                                    continue;
                                }
                            }

                            mempool.Included = false;
                            mempool.Replied = false;

                            var added = await GetInstance()._memPool.AddMemPoolTransaction(mempool);
                            if (added == null)
                            {
                                _logger.LogError($"<<< MempoolSocketService.OnMessage >>>: " +
                                    $"Blockgraph: {mempool.Block.Hash} was not add " +
                                    $"for node {mempool.Block.Node} and round {mempool.Block.Round}");
                            }
                        }
                    }

                    _cancel.Cancel();

                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< MempoolSocketService.OnMessage >>>: {ex.Message}");
            }

            Send($"Received mempool: {GetInstance()._serfClient.P2PConnectionOptions.ClientId}");
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            if (GetInstance() != null)
            {
                if (GetInstance()._wss != null)
                {
                    if (GetInstance()._wss.IsListening)
                    {
                        GetInstance()._wss.Stop();
                        GetInstance()._wss = null;
                    }
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}
