using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using CYPCore.Serf.Message;
using CYPCore.Serf.Strategies;
using Dawn;
using MessagePack;
using Serilog;

namespace CYPCore.Serf
{
    public class SerfRpcClient
    {
        private readonly ILogger _logger;

        private Socket _socket;
        private readonly IPEndPoint _endpoint;

        private readonly BehaviorSubject<ClientState> _state = new(ClientState.Initializing);
        private readonly BehaviorSubject<SerfClientState> _serfState = new(SerfClientState.Undefined);
        private readonly Subject<byte[]> _dataReceived = new();

        public IObservable<ClientState> State => _state;
        public IObservable<SerfClientState> SerfState => _serfState;
        public IObservable<IEnumerable<byte>> DataReceived => _dataReceived;

        private readonly IFormatterResolver _messagePackResolver = MessagePack.Resolvers.StandardResolver.Instance;

        public enum ClientState
        {
            Initializing,
            Connecting,
            Connected,
            Disconnected,
            FatalError
        };

        public enum SerfClientState
        {
            Undefined,
            Handshaking,
            HandshakeOk,
            Joining,
            Joined,
            Error
        };

        public SerfRpcClient(IPEndPoint endpoint, ILogger logger)
        {
            Guard.Argument(endpoint, nameof(endpoint)).NotNull();
            Guard.Argument(logger, nameof(logger)).NotNull();

            _endpoint = endpoint;
            _logger = logger;

            _logger.Debug("SerfRpcClient::SerfRpcClient");

            var connectionStrategy = new ExponentialBackoffConnectionStrategy(TimeSpan.FromSeconds(2), 10);

            #region Connection handling

            // Serf [Error] -> [Disconnected]
            _serfState
                .Where(s => s == SerfClientState.Error)
                .Subscribe(s => Disconnect());

            // State [Disconnected] -> DoReconnect
            _state
                .Where(s => s == ClientState.Disconnected)
                .Subscribe(s => connectionStrategy.DoReconnect());
            
            // DoReconnect -> [Connected, Disconnected, FatalError]
            connectionStrategy.Reconnect
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Subscribe(r =>
                {
                    Interlocked.Exchange(ref _seqId, 0);
                    Connect();
                });

            // State [Connected] -> [Handshaking]
            _state
                .Where(s => s == ClientState.Connected)
                .Subscribe(s =>
                {
                    // Initiate receive loop
                    Receive();
                    
                    connectionStrategy.ResetReconnectCounter();
                    Handshake();
                });
            
            #endregion

            #region Data handling

            // Serf [Handshaking] -> [HandshakeOk, Error]
            _dataReceived
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(d => _serfState.Value == SerfClientState.Handshaking)
                .Select(data =>
                {
                    Resolve<ResponseHeader>(data, out var responseHeader);
                    return new {Ok = IsResponseHeaderOk(responseHeader)};
                })
                .Subscribe(resolvedData =>
                {
                    _logger.Debug("Got handshake response");

                    if (resolvedData.Ok)
                    {
                        _logger.Debug("HandshakeOk");
                        _serfState.OnNext(SerfClientState.HandshakeOk);
                    }
                    else
                    {
                        _logger.Error("Handshake response not correct");
                        _serfState.OnNext(SerfClientState.Error);
                    }
                });

            // Serf [HandshakeOk] -> [Joining, Error]
            _serfState
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(d => _serfState.Value == SerfClientState.HandshakeOk)
                .Subscribe(d => Join());

            // Serf [Joining] -> [Joined, Error]
            _dataReceived
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(d => _serfState.Value == SerfClientState.Joining)
                .Select(data =>
                {
                    Resolve<JoinResponse>(
                        Resolve<ResponseHeader>(data, out var responseHeader),
                        out var joinResponse);

                    return new
                    {
                        Ok = IsResponseHeaderOk(responseHeader),
                        joinResponse
                    };
                })
                .Subscribe(resolvedData =>
                {
                    _logger.Debug("Got join response");

                    if (resolvedData.Ok)
                    {
                        _logger.Debug($"Joined: {resolvedData.joinResponse.Data.Peers} peers");
                        _serfState.OnNext(SerfClientState.Joined);
                    }
                    else
                    {
                        _logger.Error("Join response not correct");
                        _serfState.OnNext(SerfClientState.Error);
                    }
                });

            #endregion

            // Initial: Disconnected
            // Trigger connection strategy to initiate the connection on a new thread
            _state.OnNext(ClientState.Disconnected);
        }

        private ulong _seqId;
        private ulong SeqId => Interlocked.Increment(ref _seqId);

        #region Connection handling

        // State Connecting -> [Connected, Disconnected, FatalError]
        private void Connect()
        {
            _logger.Debug("SerfRpcClient::Connect");
            _state.OnNext(ClientState.Connecting);

            try
            {
                _socket = new Socket(_endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(_endpoint);
                _state.OnNext(ClientState.Connected);
            }
            catch (SocketException socketException)
            {
                _logger.Error(socketException, "Error while connecting");
                _state.OnNext(ClientState.Disconnected);
            }
            catch (ObjectDisposedException objectDisposedException)
            {
                _logger.Fatal(objectDisposedException, "Cannot use TcpClient");
                _state.OnNext(ClientState.FatalError);
            }
        }

        private void Disconnect()
        {
            _logger.Debug("SerfRpcClient::Disconnect");

            if (_socket.Connected)
            {
                _socket.Close();
            }

            _serfState.OnNext(SerfClientState.Undefined);
            _state.OnNext(ClientState.Disconnected);

            _socket = null;
        }

        #endregion

        #region Data handling

        #region Socket

        private void Send(IEnumerable<byte> data)
        {
            try
            {
                var bytesSent = _socket.Send(data.ToArray());
                _logger.Debug($"Sent {@bytesSent} bytes", bytesSent);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Cannot send");
                _serfState.OnNext(SerfClientState.Error);
            }
        }

        private void Receive()
        {
            _logger.Debug("Receiving data");

            var buffer = new byte[_socket.ReceiveBufferSize];

            try
            {
                _socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, buffer);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error while reading data");
                _serfState.OnNext(SerfClientState.Error);
                return;
            }
        }

        private void ReceiveCallback(IAsyncResult result)
        {
            _logger.Debug("Receive callback");

            var length = 0;
            try
            {
                length = _socket.EndReceive(result);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "ReceiveCallback");
                _serfState.OnNext(SerfClientState.Error);
                return;
            }

            if (length <= 0)
            {
                _serfState.OnNext(SerfClientState.Error);
                return;
            }

            var buffer = (byte[]) result.AsyncState;
            var data = new byte[length];
            Array.Copy(buffer, data, data.Length);
            _dataReceived.OnNext(data);

            Receive();
        }

        #endregion

        #region Serf RPC

        private RequestHeader GetRequestHeader(string command)
        {
            return new RequestHeader
            {
                Command = command,
                Sequence = SeqId
            };
        }

        private bool IsResponseHeaderOk(ResolvedData<ResponseHeader> responseHeader)
        {
            return responseHeader.Ok && string.IsNullOrWhiteSpace(responseHeader.Data.Error);
        }

        private void Handshake()
        {
            _serfState.OnNext(SerfClientState.Handshaking);

            Send(Serialize(
                GetRequestHeader(SerfCommandLine.Handshake),
                new Handshake
                {
                    Version = 1
                }));
        }

        private void Join()
        {
            _logger.Debug("Joining");
            
            _serfState.OnNext(SerfClientState.Joining);

            Send(Serialize(
                GetRequestHeader(SerfCommandLine.Join),
                new JoinRequest
                {
                    Existing = new[] {"67.205.161.184:7946"},
                    Replay = false
                }));
        }

        #endregion

        #region MessagePack

        private struct ResolvedData<T>
        {
            public bool Ok { get; set; }
            public T Data { get; set; }
        }

        private IEnumerable<byte> Resolve<T>(IEnumerable<byte> data, out ResolvedData<T> resolved)
        {
            var resolvedData = new ResolvedData<T>
            {
                Ok = true
            };
            
            var enumerable = data as byte[] ?? data.ToArray();
            var readSize = 0;

            try
            {
                resolvedData.Data = _messagePackResolver.GetFormatter<T>()
                    .Deserialize(enumerable.ToArray(), 0, _messagePackResolver, out readSize);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Cannot resolve data type");
                resolvedData.Ok = false;
            }

            resolved = resolvedData;

            return enumerable.Skip(readSize);
        }


        private static IEnumerable<byte> Serialize<T1, T2>(T1 t1, T2 t2)
        {
            if (t1 == null || t2 == null)
            {
                throw new Exception("Cannot serialize null object");
            }

            return MessagePackSerializer.Serialize(t1).Concat(
                MessagePackSerializer.Serialize(t2));
        }
        
        #endregion

        #endregion
    }
}