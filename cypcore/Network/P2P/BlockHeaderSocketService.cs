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
using CYPCore.Cryptography;
using CYPCore.Ledger;
using CYPCore.Persistence;

namespace CYPCore.Network.P2P
{
    public class BlockHeaderSocketService : WebSocketBehavior, IStartable, IDisposable
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signingProvider;
        private readonly IValidator _validator;
        private readonly ILogger _logger;

        private WebSocketServer _wss;
        private CancellationTokenSource _cancel;

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
                var endpoint = Helper.Util.TryParseAddress(_serfClient.P2PConnectionOptions.TcpServerBlock);

                _wss = new WebSocketServer($"ws://{endpoint.Address}:{endpoint.Port}");
                _wss.AddWebSocketService<BlockHeaderSocketService>($"/{SocketTopicType.Block}");
                _wss.Start();

                if (!_wss.IsListening)
                {
                    _logger.LogError($"<<< BlockHeaderSocketService.Start >>>: Faild to started P2P socket block header at ws://{endpoint.Address}:{endpoint.Port}");
                }
                else
                {
                    _logger.LogInformation($"<<< BlockHeaderSocketService.Start >>>: Started P2P socket block header at ws://{endpoint.Address}:{endpoint.Port}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockHeaderSocketService.Start >>: {ex.Message}");
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
                    var payloads = Enumerable.Empty<PayloadProto>();

                    try
                    {
                        if (e.RawData.Length > 26214400)
                        {
                            throw new Exception("Payload size exceeds 25MB.");
                        }

                        if (GetInstance() == null)
                        {
                            throw new Exception("Null reference exception on GetInstance()");
                        }

                        payloads = Helper.Util.DeserializeListProto<PayloadProto>(e.RawData);
                        foreach (var payload in payloads)
                        {
                            var valid = _signingProvider.VerifySignature(payload.Signature, payload.PublicKey, Helper.Util.SHA384ManagedHash(payload.Payload));
                            if (!valid)
                            {
                                throw new Exception("Signature failed to validate.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"<<< BlockHeaderSocketService.OnMessage >>>: {ex}");
                    }

                    return payloads;

                }, _cancel.Token).ContinueWith(async payload =>
                {
                    if (payload.IsCanceled || payload.IsFaulted)
                    {
                        throw new Exception(payload.Exception.Message);
                    }

                    if (payload.Result?.Any() == true)
                    {
                        foreach (PayloadProto payloadProto in payload.Result)
                        {
                            var blockHeader = Helper.Util.DeserializeProto<BlockHeaderProto>(payloadProto.Payload);
                            var valid = await _validator.VerifyBlockHeader(blockHeader);

                            if (valid)
                            {
                                var saved = await _unitOfWork.DeliveredRepository.PutAsync(blockHeader, blockHeader.ToIdentifier());
                                if (saved == null)
                                {
                                    _logger.LogError($"<<< BlockHeaderSocketService.OnMessage >>>: Unable to save block header: {blockHeader.MrklRoot}");
                                }
                            }
                        }
                    }

                    _cancel.Cancel();

                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockHeaderSocketService.OnMessage >>>: {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            if (_wss != null)
            {
                if (_wss.IsListening)
                {
                    _wss.Stop();
                    _wss = null;
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}
