// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Autofac;

using Dawn;

using WebSocketSharp;
using WebSocketSharp.Server;

using CYPCore.Models;
using CYPCore.Serf;
using CYPCore.Ledger;
using CYPCore.Helper;

namespace CYPCore.Network.P2P
{
    public class MempoolSocketService : WebSocketBehavior, IStartable, IDisposable
    {
        private readonly IMempool _memPool;
        private readonly ISerfClient _serfClient;
        private readonly ILogger _logger;
        private readonly BackgroundQueue _queue;

        private WebSocketServer _wss;

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

            _queue = new();

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
                throw new Exception("<<< MempoolSocketService.Start >>>: Null reference exception on GetInstance()");
            }

            var endpoint = Util.TryParseAddress(GetInstance()._serfClient.P2PConnectionOptions.TcpServerMempool);

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
            if (GetInstance() == null)
            {
                throw new Exception("<<< MempoolSocketService.OnMessage >>>: Null reference exception on GetInstance()");
            }

            await GetInstance()._queue.QueueTask(async () =>
            {
                try
                {
                    var memPools = Util.DeserializeListProto<MemPoolProto>(e.RawData);
                    if (memPools.Any())
                    {
                        foreach (var mempool in memPools)
                        {
                            var processed = await Process(mempool);
                            if (!processed)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        var payload = Util.DeserializeProto<MemPoolProto>(e.RawData);
                        if (payload != null)
                        {
                            await Process(payload);
                        }
                    }
                }
                catch (Exception ex)
                {
                    GetInstance()._logger.LogError($"<<< MempoolSocketService.OnMessage >>>: {ex}");
                }

                Send($"Replied from: {GetInstance()._serfClient.P2PConnectionOptions.ClientId}");
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        private static async Task<bool> Process(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            if (GetInstance() == null)
            {
                throw new Exception("<<< MempoolSocketService.Process >>>: Null reference exception on GetInstance()");
            }

            if (GetInstance()._serfClient.P2PConnectionOptions.ClientId == memPool.Block.Node)
            {
                return false;
            }

            memPool.Included = false;
            memPool.Replied = false;

            var added = await GetInstance()._memPool.AddMemPoolTransaction(memPool);
            if (added == null)
            {
                GetInstance()._logger.LogError($"<<< MempoolSocketService.Process >>>: " +
                    $"Blockgraph: {memPool.Block.Hash} was not add " +
                    $"for node {memPool.Block.Node} and round {memPool.Block.Round}");

                return false;
            }

            return true;
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
