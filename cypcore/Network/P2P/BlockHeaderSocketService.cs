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
using CYPCore.Cryptography;
using CYPCore.Ledger;
using CYPCore.Persistence;
using CYPCore.Helper;

namespace CYPCore.Network.P2P
{
    public class BlockHeaderSocketService : WebSocketBehavior, IStartable, IDisposable
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signingProvider;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly BackgroundQueue _queue;

        private WebSocketServer _wss;

        private static BlockHeaderSocketService _instance;

        public BlockHeaderSocketService()
        {
            _logger = NullLogger<BlockHeaderSocketService>.Instance;
        }

        public BlockHeaderSocketService(IUnitOfWork unitOfWork, ISerfClient serfClient, ISigning signingProvider,
            IValidator validator, ILogger<BlockHeaderSocketService> logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _signingProvider = signingProvider;
            _validator = validator;
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
        public static BlockHeaderSocketService GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start()
        {
            try
            {
                if (GetInstance() == null)
                {
                    throw new Exception("<<< BlockHeaderSocketService.Start >>>: Null reference exception on GetInstance()");
                }

                var endpoint = Util.TryParseAddress(GetInstance()._serfClient.P2PConnectionOptions.TcpServerBlock);

                GetInstance()._wss = new WebSocketServer($"ws://{endpoint.Address}:{endpoint.Port}");
                GetInstance()._wss.AddWebSocketService<BlockHeaderSocketService>($"/{SocketTopicType.Block}");
                GetInstance()._wss.Start();

                if (!_wss.IsListening)
                {
                    GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.Start >>>: Faild to started P2P socket block header at ws://{endpoint.Address}:{endpoint.Port}");
                }
                else
                {
                    GetInstance()._logger.LogInformation($"<<< BlockHeaderSocketService.Start >>>: Started P2P socket block header at ws://{endpoint.Address}:{endpoint.Port}");
                }
            }
            catch (Exception ex)
            {
                GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.Start >>>: {ex.Message}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMessage(MessageEventArgs e)
        {
            if (GetInstance() == null)
            {
                throw new Exception("<<< BlockHeaderSocketService.OnMessage >>>: Null reference exception on GetInstance()");
            }

            GetInstance()._queue.QueueTask(() =>
            {
                try
                {
                    if (e.RawData.Length > 26214400)
                    {
                        GetInstance()._logger.LogError("<<< BlockHeaderSocketService.OnMessage >>>: Payload size exceeds 25MB");
                        return;
                    }

                    var payloads = Util.DeserializeListProto<PayloadProto>(e.RawData);
                    if (payloads.Any())
                    {
                        foreach (var payload in payloads)
                        {
                            var processed = Process(payload).GetAwaiter().GetResult();
                            if (!processed)
                            {
                                GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.OnMessage >>>: Unable to process the block header");
                                break;
                            }
                        }
                    }
                    else
                    {
                        var payload = Util.DeserializeProto<PayloadProto>(e.RawData);
                        if (payload != null)
                        {
                            var processed = Process(payload).GetAwaiter().GetResult();
                            if (!processed)
                            {
                                GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.OnMessage >>>: Unable to process the block header");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.OnMessage >>>: {ex}");
                }

                Send($"Replied from: {GetInstance()._serfClient.P2PConnectionOptions.ClientId}");
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        private static async Task<bool> Process(PayloadProto payload)
        {
            Guard.Argument(payload, nameof(payload)).NotNull();

            if (GetInstance() == null)
            {
                throw new Exception("<<< MempoolSocketService.Process >>>: Null reference exception on GetInstance()");
            }

            var verified = GetInstance()._signingProvider.VerifySignature(payload.Signature, payload.PublicKey, Util.SHA384ManagedHash(payload.Payload));
            if (!verified)
            {
                GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.Process >>: Unable to verifiy signature.");
                return false;
            }

            var blockHeader = Util.DeserializeProto<BlockHeaderProto>(payload.Payload);

            await GetInstance()._validator.GetRunningDistribution();

            verified = await GetInstance()._validator.VerifyBlockHeader(blockHeader);
            if (!verified)
            {
                GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.Process >>: Unable to verifiy block header.");
            }

            var saved = await GetInstance()._unitOfWork.DeliveredRepository.PutAsync(blockHeader, blockHeader.ToIdentifier());
            if (saved == null)
            {
                GetInstance()._logger.LogError($"<<< BlockHeaderSocketService.Process >>>: Unable to save block header: {blockHeader.MrklRoot}");
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
