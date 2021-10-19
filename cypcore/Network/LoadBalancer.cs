// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using CYPCore.Consensus.Models;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Network.Commands;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetMQ;
using NetMQ.Sockets;
using Block = CYPCore.Models.Block;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public interface ILoadBalancer
    {
        /// <summary>
        /// 
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="commandId"></param>
        void NewCommand(byte commandId);

        /// <summary>
        /// 
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// 
    /// </summary>
    public class LoadBalancer : ILoadBalancer
    {
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly RouterSocket _frontend;
        private readonly RouterSocket _backend;
        private readonly ILogger _logger;
        private readonly NetMQPoller _poller;
        private readonly MemStore<byte> _memStoreSocketCommands = new();
        private readonly string _backendAddress;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="frontendAddress"></param>
        /// <param name="applicationLifetime"></param>
        /// <param name="logger"></param>
        public LoadBalancer(string frontendAddress, IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _applicationLifetime = applicationLifetime;
            _backendAddress = $"@tcp://localhost:{FindFreePort()}";
            _logger = logger;
            _frontend = new RouterSocket(frontendAddress);
            _frontend.Options.TcpKeepalive = true;
            _frontend.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(1);
            _backend = new RouterSocket(_backendAddress);
            _backend.Options.TcpKeepalive = true;
            _backend.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(1);
            _frontend.ReceiveReady += OnFrontendReady;
            _backend.ReceiveReady += OnBackendReady;
            _poller = new NetMQPoller { _frontend, _backend };
            SetupCommands();
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task StartAsync()
        {
            await Task.Run(() =>
            {
                _poller.Run();
            }, _applicationLifetime.ApplicationStopping);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="commandId"></param>
        public void NewCommand(byte commandId)
        {
            _memStoreSocketCommands.Put(commandId.ToBytes(), commandId);
        }

        /// <summary>
        /// 
        /// </summary>
        private void SetupCommands()
        {
            NewCommand((byte)CommandMessage.GetBlockCount);
            NewCommand((byte)CommandMessage.GetBlockHeight);
            NewCommand((byte)CommandMessage.GetPeer);
            NewCommand((byte)CommandMessage.GetTransaction);
            NewCommand((byte)CommandMessage.GetMemTransaction);
            NewCommand((byte)CommandMessage.GetBlocks);
            NewCommand((byte)CommandMessage.GetSafeguardBlocks);
            NewCommand((byte)CommandMessage.Transaction);
            NewCommand((byte)CommandMessage.BlockGraph);
            NewCommand((byte)CommandMessage.GetPosTransaction);
            NewCommand((byte)CommandMessage.GetTransactionBlockIndex);
        }

        /// <summary>
        /// 
        /// </summary>
        private string BackEndAddress => _backendAddress.Replace("@", "");

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private static int FindFreePort()
        {
            var port = 0;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                Task.Delay(1000);
                var localEp = new IPEndPoint(IPAddress.Any, 0);
                socket.Bind(localEp);
                localEp = (IPEndPoint)socket.LocalEndPoint;
                if (localEp is { }) port = localEp.Port;
            }
            catch (SocketException ex)
            {
                throw new SocketException(ex.ErrorCode);
            }
            finally
            {
                socket.Close();
            }
            return port;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnFrontendReady(object sender, NetMQSocketEventArgs e)
        {
            var msg = e.Socket.ReceiveMultipartMessage();
            await Task.Run(async () =>
            {
                try
                {
                    var command = Enum.Parse<CommandMessage>(msg[1].ConvertToString(), true);
                    if (!_memStoreSocketCommands.TryGet(((byte)command).ToBytes(), out _)) return;
                    Parameter[] parameters = null;
                    if (msg.Count() >= 3)
                    {
                        parameters = await Helper.Util.DeserializeAsync<Parameter[]>(msg[2].Buffer);
                    }

                    switch (command)
                    {
                        case CommandMessage.Transaction:
                            await OnNewTransaction(msg[0].ToByteArray(), parameters[0].Value);
                            break;
                        case CommandMessage.BlockGraph:
                            await OnNewBlockGraph(msg[0].ToByteArray(), parameters[0].Value);
                            break;
                        case CommandMessage.GetBlocks:
                            await OnGetBlocks(msg[0].ToByteArray(), Convert.ToInt32(parameters[0].Value.FromBytes()),
                                Convert.ToInt32(parameters[1].Value.FromBytes()));
                            break;
                        case CommandMessage.GetPeer:
                            await OnGetPeer(msg[0].ToByteArray());
                            break;
                        case CommandMessage.SaveBlock:
                            await OnSaveBlock(msg[0].ToByteArray(), parameters[0].Value);
                            break;
                        case CommandMessage.GetBlockHeight:
                            await OnGetBlockHeight(msg[0].ToByteArray());
                            break;
                        case CommandMessage.GetBlockCount:
                            await OnGetBlockCount(msg[0].ToByteArray());
                            break;
                        case CommandMessage.GetMemTransaction:
                            await OnGetMemoryPoolTransaction(msg[0].ToByteArray(), parameters[0].Value);
                            break;
                        case CommandMessage.GetTransaction:
                            await OnGetTransaction(msg[0].ToByteArray(), parameters[0].Value);
                            break;
                        case CommandMessage.GetSafeguardBlocks:
                            await OnSafeguardBlocks(msg[0].ToByteArray());
                            break;
                        case CommandMessage.GetPosTransaction:
                            await OnPosTransaction(msg[0].ToByteArray(), parameters[0].Value);
                            break;
                        case CommandMessage.GetTransactionBlockIndex:
                            await OnTransactionBlockIndex(msg[0].ToByteArray(), parameters[0].Value);
                            break;
                        default: throw new ArgumentOutOfRangeException();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                }
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        private async Task OnSafeguardBlocks(byte[] key)
        {
            var numberOfBlocks = 147; // +- block proposal time * number of blocks
            var socketCommandEx = new RequestSocketCommand<SafeguardBlocksResponse, SafeguardBlocksRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new SafeguardBlocksRequest(numberOfBlocks));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="transactionId"></param>
        private async Task OnGetTransaction(byte[] key, byte[] transactionId)
        {
            var socketCommandEx = new RequestSocketCommand<TransactionResponse, TransactionRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new TransactionRequest(transactionId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        private async Task OnGetBlockCount(byte[] key)
        {
            var socketCommandEx = new RequestSocketCommand<BlockCountResponse, BlockCountRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new BlockCountRequest());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        private async Task OnGetBlockHeight(byte[] key)
        {
            var socketCommandEx = new RequestSocketCommand<BlockHeightResponse, BlockHeightRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new BlockHeightRequest());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="transactionId"></param>
        private async Task OnGetMemoryPoolTransaction(byte[] key, byte[] transactionId)
        {
            var socketCommandEx =
                new RequestSocketCommand<MemoryPoolTransactionResponse, MemoryPoolTransactionRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new MemoryPoolTransactionRequest(transactionId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="block"></param>
        private async Task OnSaveBlock(byte[] key, byte[] block)
        {
            var socketCommandEx = new RequestSocketCommand<SaveBlockResponse, SaveBlockRequest>();
            await socketCommandEx.Execute(key, BackEndAddress,
                new SaveBlockRequest(await Helper.Util.DeserializeAsync<Block>(block)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        private async Task OnGetPeer(byte[] key)
        {
            var socketCommandEx = new RequestSocketCommand<PeerResponse, PeerRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new PeerRequest());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        private async Task OnGetBlocks(byte[] key, int skip, int take)
        {
            var socketCommandEx = new RequestSocketCommand<BlocksResponse, BlocksRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new BlocksRequest(skip, take));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="transaction"></param>
        private async Task OnNewTransaction(byte[] key, byte[] transaction)
        {
            var socketCommandEx = new RequestSocketCommand<NewTransactionResponse, NewTransactionRequest>();
            await socketCommandEx.Execute(key, BackEndAddress,
                new NewTransactionRequest(await Helper.Util.DeserializeAsync<Transaction>(transaction)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="blockGraph"></param>
        private async Task OnNewBlockGraph(byte[] key, byte[] blockGraph)
        {
            var socketCommandEx = new RequestSocketCommand<NewBlockGraphResponse, NewBlockGraphRequest>();
            await socketCommandEx.Execute(key, BackEndAddress,
                new NewBlockGraphRequest(await Helper.Util.DeserializeAsync<BlockGraph>(blockGraph)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="transactionId"></param>
        private async Task OnPosTransaction(byte[] key, byte[] transactionId)
        {
            var socketCommandEx = new RequestSocketCommand<PosPoolTransactionResponse, PosPoolTransactionRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new PosPoolTransactionRequest(transactionId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="transactionId"></param>
        private async Task OnTransactionBlockIndex(byte[] key, byte[] transactionId)
        {
            var socketCommandEx = new RequestSocketCommand<TransactionBlockIndexResponse, TransactionBlockIndexRequest>();
            await socketCommandEx.Execute(key, BackEndAddress, new TransactionBlockIndexRequest(transactionId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnBackendReady(object sender, NetMQSocketEventArgs e)
        {
            try
            {
                var message = new NetMQMessage();
                if (!e.Socket.TryReceiveMultipartMessage(ref message)) return;
                _frontend.SendMultipartMessage(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _frontend?.Dispose();
            _backend?.Dispose();
        }
    }
}