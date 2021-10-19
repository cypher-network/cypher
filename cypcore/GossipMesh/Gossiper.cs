using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CYPCore.GossipMesh
{
    /// <summary>
    /// 
    /// </summary>
    public class Gossiper : IDisposable
    {
        private const byte ProtocolVersion = 0x00;
        private readonly object _locker = new();
        private readonly Member _self;
        private readonly Dictionary<IPEndPoint, Member> _members = new();
        private volatile Dictionary<IPEndPoint, DateTime> _awaitingAcks = new();
        private DateTime _lastProtocolPeriod = DateTime.UtcNow;
        private readonly Random _rand = new();
        private UdpClient _udpClient;

        private readonly GossiperOptions _options;
        private readonly ILogger _logger;
        private readonly CancellationToken _cancellationToken;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="listenPort"></param>
        /// <param name="service"></param>
        /// <param name="servicePort"></param>
        /// <param name="options"></param>
        /// <param name="stoppingToken"></param>
        /// <param name="logger"></param>
        public Gossiper(ushort listenPort, byte service, ushort servicePort, GossiperOptions options, CancellationToken stoppingToken, ILogger logger)
        {
            _options = options;
            _cancellationToken = stoppingToken;
            _logger = logger;

            _self = new Member(MemberState.Alive, IPAddress.Any, listenPort, 1, service, servicePort);
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task StartAsync()
        {
            _logger.LogInformation("Gossip.Mesh starting Gossiper on {GossipEndPoint}", _self.GossipEndPoint);
            _udpClient = CreateUdpClient(_self.GossipEndPoint);
            PushToMemberListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));

            // start listener
            Listener();
            // bootstrap off seeds
            await Bootstrap().ConfigureAwait(false);
            // gossip
            GossipPump();
            // detect dead members and prune old members
            DeadMemberHandler();
            // low frequency ping to seeds to avoid network partitioning
            NetworkPartitionGuard();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task PingRandomSeed()
        {
            try
            {
                var i = _rand.Next(0, _options.SeedMembers.Length);
                await PingAsync(_options.SeedMembers[i]).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message,
                    ex.StackTrace);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task Bootstrap()
        {
            if (_options.SeedMembers is null || _options.SeedMembers.Length == 0)
            {
                _logger.LogInformation("Gossip.Mesh no seeds to bootstrap off");
                return;
            }

            _logger.LogInformation("Gossip.Mesh bootstrapping off seeds");
            while (IsMembersEmpty())
            {
                await PingRandomSeed().ConfigureAwait(false);
                await Task.Delay(_options.ProtocolPeriodMilliseconds, _cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Gossip.Mesh finished bootstrapping");
        }

        /// <summary>
        /// 
        /// </summary>
        private async void NetworkPartitionGuard()
        {
            if (_options.SeedMembers is null || _options.SeedMembers.Length == 0)
            {
                return;
            }

            while (true)
            {
                try
                {
                    int n;
                    lock (_locker)
                    {
                        n = _members.Count * 1000;
                    }

                    // wait the max of either 1m or 1s for each member
                    await Task.Delay(Math.Max(60000, n), _cancellationToken).ConfigureAwait(false);
                    await PingRandomSeed().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}",
                        ex.Message, ex.StackTrace);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private async void Listener()
        {
            while (true)
            {
                try
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var request = await _udpClient.ReceiveAsync().ConfigureAwait(false);
                    var receivedDateTime = DateTime.UtcNow;
                    await using var stream = new MemoryStream(request.Buffer, false);
                    var remoteProtocolVersion = stream.ReadByte();
                    if (IsVersionCompatible(request.Buffer[0]))
                    {
                        var messageType = stream.ReadMessageType();
                        _logger.LogDebug("Gossip.Mesh received {MessageType} from {RemoteEndPoint}", messageType,
                            request.RemoteEndPoint);
                        IPEndPoint destinationGossipEndPoint;
                        IPEndPoint sourceGossipEndPoint;
                        if (messageType is MessageType.Ping or MessageType.Ack)
                        {
                            sourceGossipEndPoint = request.RemoteEndPoint;
                            destinationGossipEndPoint = _self.GossipEndPoint;
                        }
                        else if (messageType is MessageType.RequestPing or MessageType.RequestAck)
                        {
                            sourceGossipEndPoint = request.RemoteEndPoint;
                            destinationGossipEndPoint = stream.ReadIPEndPoint();
                        }
                        else
                        {
                            sourceGossipEndPoint = stream.ReadIPEndPoint();
                            destinationGossipEndPoint = _self.GossipEndPoint;
                        }

                        UpdateMembers(request.RemoteEndPoint, receivedDateTime, stream);
                        await RequestHandler(request, messageType, sourceGossipEndPoint, destinationGossipEndPoint)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Gossip.Mesh received message on incompatible protocol version from {RemoteEndPoint}. Current version: {CurrentVersion} Received version: {ReceivedVersion}",
                            request.RemoteEndPoint, ProtocolVersion, remoteProtocolVersion);
                    }
                }
                catch (SocketException)
                {
                    _udpClient = CreateUdpClient(_self.GossipEndPoint);
                }
                catch (Exception ex) when (ex is not TaskCanceledException && ex is not ObjectDisposedException)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}",
                        ex.Message, ex.StackTrace);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private async void GossipPump()
        {
            while (true)
            {
                try
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var gossipEndPoints = GetRandomGossipEndPoints(_options.FanoutFactor).ToArray();
                    var gossipTasks = new Task[gossipEndPoints.Length];
                    for (var i = 0; i < gossipEndPoints.Length; i++)
                    {
                        gossipTasks[i] = Gossip(gossipEndPoints[i]);
                    }

                    await Task.WhenAll(gossipTasks).ConfigureAwait(false);
                    await WaitForProtocolPeriod().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}",
                        ex.Message, ex.StackTrace);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gossipEndPoint"></param>
        private async Task Gossip(IPEndPoint gossipEndPoint)
        {
            try
            {
                AddAwaitingAck(gossipEndPoint);
                await PingAsync(gossipEndPoint).ConfigureAwait(false);
                await Task.Delay(_options.AckTimeoutMilliseconds, _cancellationToken).ConfigureAwait(false);
                if (WasNotAcked(gossipEndPoint))
                {
                    var indirectEndpoints =
                        GetRandomGossipEndPoints(_options.NumberOfIndirectEndpoints, gossipEndPoint);
                    await RequestPingAsync(gossipEndPoint, indirectEndpoints).ConfigureAwait(false);
                    await PingAsync(gossipEndPoint).ConfigureAwait(false);
                    await Task.Delay(_options.AckTimeoutMilliseconds, _cancellationToken).ConfigureAwait(false);
                }

                if (WasNotAcked(gossipEndPoint))
                {
                    UpdateMemberState(gossipEndPoint, MemberState.Suspicious);
                }
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}", ex.Message,
                    ex.StackTrace);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private async void DeadMemberHandler()
        {
            while (true)
            {
                try
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    lock (_locker)
                    {
                        foreach (var awaitingAck in _awaitingAcks.ToArray())
                        {
                            if (DateTime.UtcNow > awaitingAck.Value.AddMilliseconds(_options.PruneTimeoutMilliseconds))
                            {
                                if (_members.TryGetValue(awaitingAck.Key, out var member) &&
                                    (member.State == MemberState.Dead || member.State == MemberState.Left))
                                {
                                    _members.Remove(awaitingAck.Key);
                                    _logger.LogInformation("Gossip.Mesh pruned member {member}", member);
                                    member.Update(MemberState.Pruned);
                                    PushToMemberListeners(
                                        new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member));
                                }

                                _awaitingAcks.Remove(awaitingAck.Key);
                            }
                            else if (DateTime.UtcNow >
                                     awaitingAck.Value.AddMilliseconds(_options.DeadTimeoutMilliseconds))
                            {
                                UpdateMemberState(awaitingAck.Key, MemberState.Dead);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}",
                        ex.Message, ex.StackTrace);
                }

                await Task.Delay(1000, _cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="messageType"></param>
        /// <param name="sourceGossipEndPoint"></param>
        /// <param name="destinationGossipEndPoint"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private async Task RequestHandler(UdpReceiveResult request, MessageType messageType,
            IPEndPoint sourceGossipEndPoint, IPEndPoint destinationGossipEndPoint)
        {
            switch (messageType)
            {
                case MessageType.Ping:
                    await AckAsync(sourceGossipEndPoint).ConfigureAwait(false);
                    break;
                case MessageType.Ack:
                    RemoveAwaitingAck(sourceGossipEndPoint);
                    break;
                case MessageType.RequestPing:
                    await ForwardMessageAsync(MessageType.ForwardedPing, destinationGossipEndPoint,
                        sourceGossipEndPoint).ConfigureAwait(false);
                    break;
                case MessageType.RequestAck:
                    await ForwardMessageAsync(MessageType.ForwardedAck, destinationGossipEndPoint, sourceGossipEndPoint)
                        .ConfigureAwait(false);
                    break;
                case MessageType.ForwardedPing:
                    await RequestAckAsync(sourceGossipEndPoint, request.RemoteEndPoint).ConfigureAwait(false);
                    break;
                case MessageType.ForwardedAck:
                    RemoveAwaitingAck(sourceGossipEndPoint);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(messageType), messageType, null);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="messageType"></param>
        /// <param name="destinationGossipEndPoint"></param>
        private async Task SendMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint}", messageType,
                destinationGossipEndPoint);
            await using var stream = new MemoryStream(_options.MaxUdpPacketBytes);
            stream.WriteByte(ProtocolVersion);
            stream.WriteByte((byte)messageType);
            WriteMembers(stream, destinationGossipEndPoint);
            await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="messageType"></param>
        /// <param name="destinationGossipEndPoint"></param>
        /// <param name="indirectGossipEndPoint"></param>
        private async Task RequestMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint,
            IPEndPoint indirectGossipEndPoint)
        {
            _logger.LogDebug("Gossip.Mesh sending {messageType} to {destinationGossipEndPoint} via {indirectEndpoint}",
                messageType, destinationGossipEndPoint, indirectGossipEndPoint);
            await using var stream = new MemoryStream(_options.MaxUdpPacketBytes);
            stream.WriteByte(ProtocolVersion);
            stream.WriteByte((byte)messageType);
            stream.WriteIPEndPoint(destinationGossipEndPoint);
            WriteMembers(stream, indirectGossipEndPoint);
            await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, indirectGossipEndPoint)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="messageType"></param>
        /// <param name="destinationGossipEndPoint"></param>
        /// <param name="sourceGossipEndPoint"></param>
        private async Task ForwardMessageAsync(MessageType messageType, IPEndPoint destinationGossipEndPoint,
            IPEndPoint sourceGossipEndPoint)
        {
            _logger.LogDebug(
                "Gossip.Mesh sending {messageType} to {destinationGossipEndPoint} from {sourceGossipEndPoint}",
                messageType, destinationGossipEndPoint, sourceGossipEndPoint);
            await using var stream = new MemoryStream(_options.MaxUdpPacketBytes);
            stream.WriteByte(ProtocolVersion);
            stream.WriteByte((byte)messageType);
            stream.WriteIPEndPoint(sourceGossipEndPoint);
            WriteMembers(stream, destinationGossipEndPoint);
            await _udpClient.SendAsync(stream.GetBuffer(), (int)stream.Position, destinationGossipEndPoint)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="destinationGossipEndPoint"></param>
        private async Task PingAsync(IPEndPoint destinationGossipEndPoint)
        {
            await SendMessageAsync(MessageType.Ping, destinationGossipEndPoint).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="destinationGossipEndPoint"></param>
        private async Task AckAsync(IPEndPoint destinationGossipEndPoint)
        {
            await SendMessageAsync(MessageType.Ack, destinationGossipEndPoint).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="destinationGossipEndPoint"></param>
        /// <param name="indirectGossipEndPoints"></param>
        private async Task RequestPingAsync(IPEndPoint destinationGossipEndPoint, IEnumerable<IPEndPoint> indirectGossipEndPoints)
        {
            foreach (var indirectGossipEndPoint in indirectGossipEndPoints)
            {
                await RequestMessageAsync(MessageType.RequestPing, destinationGossipEndPoint, indirectGossipEndPoint).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="destinationGossipEndPoint"></param>
        /// <param name="indirectGossipEndPoint"></param>
        private async Task RequestAckAsync(IPEndPoint destinationGossipEndPoint, IPEndPoint indirectGossipEndPoint)
        {
            await RequestMessageAsync(MessageType.RequestAck, destinationGossipEndPoint, indirectGossipEndPoint).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="senderGossipEndPoint"></param>
        /// <param name="receivedDateTime"></param>
        /// <param name="stream"></param>
        private void UpdateMembers(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Stream stream)
        {
            // read sender
            var memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream, true);

            // handle ourself
            var selfClaimedState = stream.ReadMemberState();
            var selfClaimedGeneration = (byte)stream.ReadByte();
            if (_self.IsLaterGeneration(selfClaimedGeneration) ||
                (selfClaimedState != MemberState.Alive && selfClaimedGeneration == _self.Generation))
            {
                PushToMemberListeners(new MemberEvent(senderGossipEndPoint, receivedDateTime, _self.IP,
                    _self.GossipPort, selfClaimedState, selfClaimedGeneration));
                _self.Generation = (byte)(selfClaimedGeneration + 1);
                _logger.LogDebug(
                    "Gossip.Mesh received a claim about self, state:{state} generation:{generation}. Raising generation to {generation}",
                    selfClaimedState, selfClaimedGeneration, _self.Generation);
                PushToMemberListeners(new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, _self));
            }

            // handler sender and everyone else
            while (memberEvent != null)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                lock (_locker)
                {
                    if (_members.TryGetValue(memberEvent.GossipEndPoint, out var member) &&
                        (member.IsLaterGeneration(memberEvent.Generation) ||
                         (member.Generation == memberEvent.Generation && member.IsStateSuperseded(memberEvent.State))))
                    {
                        // stops state escalation
                        if (memberEvent.State == MemberState.Alive && memberEvent.Generation > member.Generation)
                        {
                            RemoveAwaitingAck(memberEvent.GossipEndPoint);
                        }

                        member.Update(memberEvent);
                        PushToMemberListeners(memberEvent);
                    }
                    else if (member == null)
                    {
                        member = new Member(memberEvent);
                        _members.Add(member.GossipEndPoint, member);
                        _logger.LogInformation("Gossip.Mesh member added {member}", member);
                        PushToMemberListeners(memberEvent);
                    }

                    if (member.State != MemberState.Alive)
                    {
                        AddAwaitingAck(member.GossipEndPoint);
                    }
                }

                memberEvent = MemberEvent.ReadFrom(senderGossipEndPoint, receivedDateTime, stream);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="destinationGossipEndPoint"></param>
        private void WriteMembers(Stream stream, IPEndPoint destinationGossipEndPoint)
        {
            lock (_locker)
            {
                stream.WriteByte(_self.Generation);
                stream.WriteByte(_self.Service);
                stream.WritePort(_self.ServicePort);
                if (_members.TryGetValue(destinationGossipEndPoint, out var destinationMember))
                {
                    stream.WriteByte((byte)destinationMember.State);
                    stream.WriteByte(destinationMember.Generation);
                }
                else
                {
                    stream.WriteByte((byte)MemberState.Alive);
                    stream.WriteByte(0x01);
                }

                var members = GetMembers(destinationGossipEndPoint);
                if (members is null) return;
                var i = 0;
                {
                    while (i < members.Length && stream.Position < _options.MaxUdpPacketBytes - 11)
                    {
                        members[i].WriteTo(stream);
                        i++;
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="listenEndPoint"></param>
        /// <returns></returns>
        private UdpClient CreateUdpClient(EndPoint listenEndPoint)
        {
            var udpClient = new UdpClient();
            try
            {
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(listenEndPoint);
                udpClient.DontFragment = true;
            }
            catch (Exception ex)
            {
                if (ex.Message != "Operation not supported")
                {
                    _logger.LogError(ex, "Gossip.Mesh threw an unhandled exception \n{message} \n{stacktrace}",
                        ex.Message, ex.StackTrace);
                }
            }

            return udpClient;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="destinationGossipEndPoint"></param>
        /// <returns></returns>
        private Member[] GetMembers(IPEndPoint destinationGossipEndPoint)
        {
            lock (_locker)
            {
                return _members.Values.OrderBy(m => m.GossipCounter).Where(m =>
                    !(_awaitingAcks.TryGetValue(m.GossipEndPoint, out var t) &&
                      DateTime.UtcNow > t.AddMilliseconds(_options.DeadCoolOffMilliseconds)) &&
                    !EndPointsMatch(destinationGossipEndPoint, m.GossipEndPoint)).ToArray();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool IsMembersEmpty()
        {
            lock (_locker)
            {
                return _members.Count == 0;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="numberOfEndPoints"></param>
        /// <param name="directGossipEndPoint"></param>
        /// <returns></returns>
        private IEnumerable<IPEndPoint> GetRandomGossipEndPoints(int numberOfEndPoints,
            IPEndPoint directGossipEndPoint = null)
        {
            var members = GetMembers(directGossipEndPoint);
            var randomIndex = _rand.Next(0, members.Length);
            if (members.Length == 0)
            {
                return Enumerable.Empty<IPEndPoint>();
            }

            return Enumerable.Range(randomIndex, numberOfEndPoints)
                .Select(ri => ri % members.Length) // wrap the range around to 0 if we hit the end
                .Select(i => members[i]).Select(m => m.GossipEndPoint).Distinct().Take(numberOfEndPoints);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gossipEndPoint"></param>
        /// <param name="memberState"></param>
        private void UpdateMemberState(IPEndPoint gossipEndPoint, MemberState memberState)
        {
            lock (_locker)
            {
                if (!_members.TryGetValue(gossipEndPoint, out var member) || member.State >= memberState) return;
                member.Update(memberState);
                if (memberState is MemberState.Dead or MemberState.Left or MemberState.Alive or MemberState.Left)
                {
                    _logger.LogInformation("Gossip.Mesh {memberState} member {member}",
                        memberState.ToString().ToLower(), member);
                }

                var memberEvent = new MemberEvent(_self.GossipEndPoint, DateTime.UtcNow, member);
                PushToMemberListeners(memberEvent);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gossipEndPoint"></param>
        private void AddAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_locker)
            {
                if (!_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Add(gossipEndPoint, DateTime.UtcNow);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gossipEndPoint"></param>
        /// <returns></returns>
        private bool WasNotAcked(IPEndPoint gossipEndPoint)
        {
            var wasNotAcked = false;
            lock (_locker)
            {
                wasNotAcked = _awaitingAcks.ContainsKey(gossipEndPoint);
            }

            return wasNotAcked;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gossipEndPoint"></param>
        private void RemoveAwaitingAck(IPEndPoint gossipEndPoint)
        {
            lock (_locker)
            {
                if (_awaitingAcks.ContainsKey(gossipEndPoint))
                {
                    _awaitingAcks.Remove(gossipEndPoint);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ipEndPointA"></param>
        /// <param name="ipEndPointB"></param>
        /// <returns></returns>
        private bool EndPointsMatch(IPEndPoint ipEndPointA, IPEndPoint ipEndPointB)
        {
            return ipEndPointA?.Port == ipEndPointB?.Port && ipEndPointA.Address.Equals(ipEndPointB.Address);
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task WaitForProtocolPeriod()
        {
            var syncTime = Math.Max(_options.ProtocolPeriodMilliseconds - (int)(DateTime.UtcNow - _lastProtocolPeriod).TotalMilliseconds, 0);
            await Task.Delay(syncTime, _cancellationToken).ConfigureAwait(false);
            _lastProtocolPeriod = DateTime.UtcNow;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        private void PushToMemberListeners(MemberEvent memberEvent)
        {
            foreach (var listener in _options.MemberListeners)
            {
                listener.MemberUpdatedCallback(memberEvent).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="version"></param>
        /// <returns></returns>
        private bool IsVersionCompatible(byte version)
        {
            // can add more complex mapping for backwards compatibility
            return ProtocolVersion == version;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _udpClient?.Close();
        }
    }
}