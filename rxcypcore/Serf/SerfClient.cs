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
using Stream = rxcypcore.Serf.Messages.Stream;

namespace rxcypcore.Serf
{
    [MessagePackObject]
    public class MemberEvent
    {
        public MemberEvent()
        {
        }

        public MemberEvent(EventType eventType, Member member)
        {
            Type = eventType;
            Member = member;

        }
        public enum EventType
        {
            Join,
            Leave,
            Failed,
            Unknown,
        };

        private static readonly Dictionary<string, EventType> EventTypeText = new()
        {
            { "member-join", EventType.Join },
            { "member-leave", EventType.Leave },
            { "member-failed", EventType.Failed }
        };

        public static EventType FromString(string text)
        {
            if (EventTypeText.TryGetValue(text, out var eventType))
            {
                return eventType;
            }

            return EventType.Unknown;
        }

        [Key("Type")]
        public EventType Type { get; set; }

        [Key("Member")]
        public Member Member { get; set; }
    }

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
        public MemberList Members { get; }

        public void Start();
        public void Stop();

        public void Join();
        public void Leave();
    }

    public class CommandData
    {
        public CommandData(Commands.SerfCommand? command, string? metadata = null)
        {
            Command = command;
            Metadata = metadata;
        }

        public Commands.SerfCommand? Command { get; }
        public string? Metadata { get; }
    }

    public class ProcessedData
    {
        public ProcessedData(ulong seq, CommandData command, IEnumerable<byte> payload)
        {
            Seq = seq;
            Command = command;
            Payload = payload;
        }

        public ulong Seq { get; }
        public CommandData Command { get; }
        public IEnumerable<byte> Payload { get; }
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
        private readonly Subject<ProcessedData> _processedData = new();
        private readonly MemberList _members = new();
        private readonly IDictionary<ulong, CommandData> _commands = new Dictionary<ulong, CommandData>();

        public IObservable<ISerfClient.ClientState> State => _state;
        public IObservable<ISerfClient.SerfClientState> SerfState => _serfState;
        public IObservable<IEnumerable<byte>> DataReceived => _dataReceived;
        public MemberList Members => _members;

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
                    ClearCommands();
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

            // State [Leaving] -> [Disconnected]
            _serfState
                .Where(state => state == ISerfClient.SerfClientState.Leaving)
                .Subscribe(state =>
                {
                    _logger.Here().Debug("Leaving serf");
                    _serfState.OnNext(ISerfClient.SerfClientState.Undefined);
                });

            #endregion

            #region Data handling

            _dataReceived
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Select(data =>
                {
                    var payload = Resolve<ResponseHeader>(data, out var responseHeader);

                    if (responseHeader == null)
                    {
                        return null;
                    }

                    _logger.Here().Debug("Received response for sequence {@SeqId}", responseHeader.Seq);

                    return new ProcessedData(
                        responseHeader.Seq,
                        GetCommand(responseHeader.Seq),
                        payload);

                })
                .Subscribe(processedData =>
                {
                    if (processedData == null)
                    {
                        _logger.Here().Error("Received empty or unexpected response payload");
                    }
                    else
                    {
                        _processedData.OnNext(processedData);
                    }
                });

            // Serf [Handshaking] -> [HandshakeOk, Error]
            _processedData
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(data => data.Command.Command == Commands.SerfCommand.Handshake)
                .Subscribe(data =>
                {
                    _logger.Here().Debug("Got handshake response");

                    if (_serfState.Value == ISerfClient.SerfClientState.Handshaking)
                    {
                        DeleteCommand(data.Seq);

                        _logger.Here().Information("Response valid");
                        _serfState.OnNext(ISerfClient.SerfClientState.HandshakeOk);
                        Join();
                    }
                    else
                    {
                        _logger.Here().Error("Received unexpected handshake response");
                        _serfState.OnNext(ISerfClient.SerfClientState.Error);
                    }
                });

            // Serf [Joining] -> [Joined, Error]
            _processedData
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(data => data.Command.Command == Commands.SerfCommand.Join)
                .Subscribe(data =>
                {
                    _logger.Here().Information("Got join response");

                    if (_serfState.Value == ISerfClient.SerfClientState.Joining)
                    {
                        DeleteCommand(data.Seq);

                        _logger.Here().Debug("Response valid");
                        _serfState.OnNext(ISerfClient.SerfClientState.Joined);
                        RegisterStream("member-join");
                        RegisterStream("member-leave");
                        RegisterStream("member-failed");

                        GetMembers();
                    }
                    else
                    {
                        _logger.Here().Error("Received unexpected join response");
                    }
                });

            // Get members
            _processedData
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(data => data.Command.Command == Commands.SerfCommand.Members)
                .Subscribe(data =>
                {
                    _logger.Here().Information("Got members response {@State}", _serfState.Value);

                    if (_serfState.Value == ISerfClient.SerfClientState.Joined)
                    {
                        DeleteCommand(data.Seq);

                        var _ = Resolve<MembersResponse>(data.Payload, out var memberData);

                        if (memberData != null)
                        {
                            Members.Clear();
                            foreach (var member in memberData.Members)
                            {
                                Members.Add(member);
                            }
                        }
                        else
                        {
                            _logger.Here().Error("Could not deserialize members payload");
                        }
                    }
                    else
                    {
                        _logger.Here().Error("Received unexpected members response");
                    }
                });

            // member event
            _processedData
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Where(data => data.Command.Command == Commands.SerfCommand.Stream && data.Command.Metadata != null &&
                               data.Command.Metadata.StartsWith("member-"))
                .Subscribe(data =>
                {
                    if (data.Payload.Any())
                    {
                        var _ = Resolve<MembersResponse>(data.Payload, out var memberData);
                        HandleMemberEvent(MemberEvent.FromString(data.Command.Metadata), memberData);
                    }
                    else
                    {
                        _logger.Here().Debug("Successfully registered on stream {@Event}", data.Command.Metadata);
                    }
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

        private void ClearCommands()
        {
            Interlocked.Exchange(ref _seqId, 0);
            _commands.Clear();
        }

        private void AddCommand(ulong seq, CommandData command)
        {
            _commands.Add(seq, command);
        }

        private CommandData GetCommand(ulong seq)
        {
            if (_commands.TryGetValue(seq, out var command))
            {
                return command;
            }

            return null;
        }

        private void DeleteCommand(ulong seq)
        {
            if (_commands.ContainsKey(seq))
            {
                _commands.Remove(seq);
            }
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
                _logger.Here().Debug("Sent {@BytesSent} bytes", bytesSent);
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

        private RequestHeader GetRequestHeader(CommandData command)
        {
            var seq = SeqId;

            _logger.Here().Debug("Using seq {@Seq}", seq);

            AddCommand(seq, command);

            return new()
            {
                Command = Commands.SerfCommandString(command.Command),
                Sequence = seq
            };
        }

        private void Handshake()
        {
            _serfState.OnNext(ISerfClient.SerfClientState.Handshaking);

            Send(Serialize(
                GetRequestHeader(new CommandData(Commands.SerfCommand.Handshake)),
                new Handshake
                {
                    Version = 1
                }));
        }

        public void Join()
        {
            _logger.Here().Debug("Joining");

            _serfState.OnNext(ISerfClient.SerfClientState.Joining);

            Send(Serialize(
                GetRequestHeader(new CommandData(Commands.SerfCommand.Join)),
                new JoinRequest
                {
                    Existing = _configuration.Clusters[0].SeedNodes.Select(
                        node => $"{node.IPAddress}:{node.Port.ToString()}"
                    ).ToArray(),

                    Replay = false
                }));
        }

        public void Leave()
        {
            _logger.Here().Debug("Leaving");

            _serfState.OnNext(ISerfClient.SerfClientState.Leaving);

            Send(Serialize(GetRequestHeader(new CommandData(Commands.SerfCommand.Leave))));
        }

        private void RegisterStream(string eventType)
        {
            _logger.Here().Debug("Registering for stream event {@Event}", eventType);

            Send(Serialize(
                GetRequestHeader(new CommandData(Commands.SerfCommand.Stream, eventType)),
                new Stream.StreamRequest
                {
                    Type = eventType
                }));
        }

        private void GetMembers()
        {
            _logger.Here().Debug("Getting members");

            Send(Serialize((GetRequestHeader(new CommandData(Commands.SerfCommand.Members)))));
        }

        private void HandleMemberEvent(MemberEvent.EventType eventType, MembersResponse memberData)
        {
            if (memberData == null)
            {
                return;
            }

            foreach (var member in memberData.Members)
            {
                switch (eventType)
                {
                    case MemberEvent.EventType.Join:
                        Members.Add(member);
                        break;

                    case MemberEvent.EventType.Leave:
                        Members.Remove(member);
                        break;

                    case MemberEvent.EventType.Failed:
                        Members.Failed(member);
                        break;
                }
            }
        }

        #endregion

        #region MessagePack

        private IEnumerable<byte> Resolve<T>(IEnumerable<byte> data, out T resolved) where T : class?
        {
            var enumerable = data as byte[] ?? data.ToArray();

            var bytesRead = 0;
            try
            {
                var resolver = CompositeResolver.Create(StandardResolver.Instance);
                var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
                resolved = MessagePackSerializer.Deserialize<T>(data.ToArray(), options, out bytesRead);
            }
            catch (Exception exception)
            {
                _logger.Here().Error(exception, "Cannot resolve data type");
                resolved = null;
            }

            return enumerable.Skip(bytesRead);
        }

        private static IEnumerable<byte> Serialize<T>(T t)
        {
            if (t == null)
            {
                throw new Exception("Cannot serialize null object");
            }

            return MessagePackSerializer.Serialize(t);
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