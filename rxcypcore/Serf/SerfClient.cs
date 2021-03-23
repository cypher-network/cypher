using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;

using Dawn;
using MessagePack;
using MessagePack.Resolvers;
using rxcypcore.Extensions;
using Serilog;

using rxcypcore.Models;
using rxcypcore.Serf.Messages;
using rxcypcore.Serf.Strategies;

namespace rxcypcore.Serf
{
    public interface ISerfClient
    {
        enum ClientState
        {
            Initializing,
            Connecting,
            Connected,
            Disconnected,
            FatalError
        };

        enum SerfClientState
        {
            Undefined,
            Handshaking,
            HandshakeOk,
            Joining,
            Joined,
            Leaving,
            Error
        };

        public IObservable<ClientState> State { get; }
        public IObservable<SerfClientState> SerfState { get; }

        public void Start();
        public void Stop();
    }

    public class SerfClient : ISerfClient
    {
        private readonly ILogger _logger;
        private readonly SerfConfigurationOptions _configuration;

        private Socket _socket;
        private readonly IPEndPoint _endPoint;

        private readonly BehaviorSubject<ISerfClient.ClientState> _state = new(ISerfClient.ClientState.Initializing);
        private readonly BehaviorSubject<ISerfClient.SerfClientState> _serfState = new(ISerfClient.SerfClientState.Undefined);
        private readonly Subject<byte[]> _dataReceived = new();

        public IObservable<ISerfClient.ClientState> State => _state;
        public IObservable<ISerfClient.SerfClientState> SerfState => _serfState;
        public IObservable<IEnumerable<byte>> DataReceived => _dataReceived;

        public SerfClient(SerfConfigurationOptions configuration, ILogger logger)
        {
            Guard.Argument(configuration, nameof(configuration)).NotNull();
            Guard.Argument(configuration.Enabled).True();
            Guard.Argument(logger, nameof(logger)).NotNull();

            _configuration = configuration;

            var ipAddress = IPAddress.Parse(configuration.RPC.IPAddress);
            _endPoint = new IPEndPoint(ipAddress, configuration.RPC.Port);

            _logger = logger.ForContext("SourceContext", nameof(SerfClient));

            _logger.Here().Debug("Starting SerfRxClient");

            var connectionStrategy = new ExponentialBackoffConnectionStrategy(TimeSpan.FromSeconds(2), 10);

            #region Connection handling

            // Serf [Error] -> [Disconnected]
            _serfState
                .Where(s => s == ISerfClient.SerfClientState.Error)
                .Subscribe(s => Disconnect());

            // State [Disconnected] -> DoReconnect
            _state
                .Where(s => s == ISerfClient.ClientState.Disconnected)
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
                .Where(s => s == ISerfClient.ClientState.Connected)
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
                .Where(d => _serfState.Value == ISerfClient.SerfClientState.Handshaking)
                .Select(data =>
                {
                    Resolve<ResponseHeader>(data, out var responseHeader);
                    return new { Ok = IsResponseHeaderOk(responseHeader) };
                })
                .Subscribe(resolvedData =>
                {
                    _logger.Here().Debug("Got handshake response");

                    if (resolvedData.Ok)
                    {
                        _logger.Here().Debug("HandshakeOk");
                        _serfState.OnNext(ISerfClient.SerfClientState.HandshakeOk);
                    }
                    else
                    {
                        _logger.Here().Error("Handshake response not correct");
                        _serfState.OnNext(ISerfClient.SerfClientState.Error);
                    }
                });

            // Serf [HandshakeOk] -> [Joining, Error]
            _serfState
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(d => _serfState.Value == ISerfClient.SerfClientState.HandshakeOk)
                .Subscribe(d => Join());

            // Serf [Joining] -> [Joined, Error]
            _dataReceived
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(d => _serfState.Value == ISerfClient.SerfClientState.Joining)
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
                    _logger.Here().Debug("Got join response");

                    if (resolvedData.Ok)
                    {
                        _logger.Here().Debug($"Joined: {resolvedData.joinResponse.Data.Peers} peers");
                        _serfState.OnNext(ISerfClient.SerfClientState.Joined);
                    }
                    else
                    {
                        _logger.Here().Error("Could not join seed nodes");
                        _serfState.OnNext(ISerfClient.SerfClientState.Error);
                    }
                });

            _serfState
                .Where(state => state == ISerfClient.SerfClientState.Leaving)
                .Subscribe(state =>
                {
                    _logger.Here().Debug("Leaving serf");
                });

            #endregion

        }

        public void Start()
        {
            // Initial: Disconnected
            // Trigger connection strategy to initiate the connection on a new thread
            _state.OnNext(ISerfClient.ClientState.Disconnected);
        }

        public void Stop()
        {
            Leave();
        }

        private ulong _seqId;
        private ulong SeqId => Interlocked.Increment(ref _seqId);

        #region Connection handling

        // State Connecting -> [Connected, Disconnected, FatalError]
        private void Connect()
        {
            _logger.Here().Debug("SerfRpcClient::Connect");
            _state.OnNext(ISerfClient.ClientState.Connecting);

            try
            {
                _socket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(_endPoint);
                _state.OnNext(ISerfClient.ClientState.Connected);
            }
            catch (SocketException socketException)
            {
                _logger.Here().Error(socketException, "Error while connecting");
                _state.OnNext(ISerfClient.ClientState.Disconnected);
            }
            catch (ObjectDisposedException objectDisposedException)
            {
                _logger.Here().Fatal(objectDisposedException, "Cannot use TcpClient");
                _state.OnNext(ISerfClient.ClientState.FatalError);
            }
        }

        private void Disconnect()
        {
            _logger.Here().Debug("SerfRpcClient::Disconnect");

            if (_socket.Connected)
            {
                _socket.Close();
            }

            _serfState.OnNext(ISerfClient.SerfClientState.Undefined);
            _state.OnNext(ISerfClient.ClientState.Disconnected);

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
                _logger.Here().Debug($"Sent {@bytesSent} bytes", bytesSent);
            }
            catch (Exception exception)
            {
                _logger.Here().Error(exception, "Cannot send");
                _serfState.OnNext(ISerfClient.SerfClientState.Error);
            }
        }

        private void Receive()
        {
            _logger.Here().Debug("Receiving data");

            var buffer = new byte[_socket.ReceiveBufferSize];

            try
            {
                _socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, buffer);
            }
            catch (Exception exception)
            {
                _logger.Here().Error(exception, "Error while reading data");
                _serfState.OnNext(ISerfClient.SerfClientState.Error);
            }
        }

        private void ReceiveCallback(IAsyncResult result)
        {
            _logger.Here().Debug("Receive callback");

            var length = 0;
            try
            {
                length = _socket.EndReceive(result);
            }
            catch (Exception exception)
            {
                _logger.Here().Error(exception, "ReceiveCallback");
                return;
            }

            if (length <= 0)
            {
                _serfState.OnNext(ISerfClient.SerfClientState.Error);
                return;
            }

            var buffer = (byte[])result.AsyncState;
            var data = new byte[length];
            Array.Copy(buffer, data, data.Length);
            _dataReceived.OnNext(data);

            Receive();
        }

        #endregion

        #region Serf RPC

        private RequestHeader GetRequestHeader(string command)
        {
            return new()
            {
                Command = command,
                Sequence = SeqId
            };
        }

        private static bool IsResponseHeaderOk(ResolvedData<ResponseHeader> responseHeader)
        {
            return responseHeader.Ok && string.IsNullOrWhiteSpace(responseHeader.Data.Error);
        }

        private void Handshake()
        {
            _serfState.OnNext(ISerfClient.SerfClientState.Handshaking);

            Send(Serialize(
                GetRequestHeader(SerfCommandLine.Handshake),
                new Handshake
                {
                    Version = 1
                }));
        }

        private void Join()
        {
            _logger.Here().Debug("Joining");

            _serfState.OnNext(ISerfClient.SerfClientState.Joining);

            Send(Serialize(
                GetRequestHeader(SerfCommandLine.Join),
                new JoinRequest
                {
                    Existing = _configuration.Clusters[0].SeedNodes.Select(
                        node => $"{node.IPAddress}:{node.Port.ToString()}"
                    ).ToArray(),

                    Replay = false
                }));
        }

        private void Leave()
        {
            _logger.Here().Debug("Joining");

            _serfState.OnNext(ISerfClient.SerfClientState.Leaving);

            Send(Serialize(GetRequestHeader(SerfCommandLine.Leave), new LeaveRequest()));
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

            var bytesRead = 0;
            try
            {
                var resolver = CompositeResolver.Create(StandardResolver.Instance);
                var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
                resolvedData.Data = MessagePackSerializer.Deserialize<T>(data.ToArray(), options, out bytesRead);
            }
            catch (Exception exception)
            {
                _logger.Here().Error(exception, "Cannot resolve data type");
                resolvedData.Ok = false;
            }

            resolved = resolvedData;

            return enumerable.Skip(bytesRead);
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