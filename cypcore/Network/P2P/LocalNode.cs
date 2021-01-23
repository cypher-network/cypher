// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;

using Serilog;

using WebSocketSharp;

using CYPCore.Serf;
using CYPCore.Models;

namespace CYPCore.Network.P2P
{
    public class LocalNode : ILocalNode
    {
        private readonly ConcurrentDictionary<ulong, List<PeerSocket>> _peers;
        private readonly ISerfClient _serfClient;
        private readonly ILogger _logger;
        private TcpSession _tcpSession;

        public LocalNode(ISerfClient serfClient, ILogger logger)
        {
            _serfClient = serfClient;
            _logger = logger;
            _peers = new ConcurrentDictionary<ulong, List<PeerSocket>>();
        }

        public void Ready()
        {
            _tcpSession = _serfClient.TcpSessionsAddOrUpdate(
                new TcpSession(_serfClient.SerfConfigurationOptions.Listening)
                .Connect(_serfClient.SerfConfigurationOptions.RPC));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task BootstrapNodes()
        {
            try
            {
                var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                if (tcpSession.Ready)
                {
                    var connectResult = _serfClient.Connect(tcpSession.SessionId);
                    var membersResult = await _serfClient.Members(tcpSession.SessionId);

                    if (!membersResult.Success)
                    {
                        return;
                    }

                    foreach (var member in membersResult.Value.Members
                        .Where(member => !_peers.TryGetValue(Helper.Util.HashToId(member.Tags["pubkey"]), out List<PeerSocket> ws)).Select(member => member))
                    {
                        if (_serfClient.Name == member.Name)
                            continue;

                        if (member.Status != "alive")
                            continue;

                        var peerSockets = new List<PeerSocket>();
                        var address = new IPAddress(member.Address).MapToIPv4();

                        var port = member.Tags["p2pblockport"];
                        var webSocket = new WebSocket($"ws://{address}:{Convert.ToInt32(port)}/{SocketTopicType.Block}");
                        
                        peerSockets.Add(new PeerSocket { Socket = webSocket, TopicType = SocketTopicType.Block });

                        port = member.Tags["p2pmempoolport"];
                        webSocket = new WebSocket($"ws://{address}:{Convert.ToInt32(port)}/{SocketTopicType.Mempool}");

                        peerSockets.Add(new PeerSocket { Socket = webSocket, TopicType = SocketTopicType.Mempool });

                        if (!_peers.TryAdd(Helper.Util.HashToId(member.Tags["pubkey"]), peerSockets))
                        {
                            _logger.Error($"<<< LocalNode.Connect >>>: Failed adding or exists in remote nodes: {member.Name}");
                            return;
                        }
                    }

                    foreach (var node in _peers
                        .Where(node => !membersResult.Value.Members.ToDictionary(x => Helper.Util.HashToId(x.Tags["pubkey"])).TryGetValue(node.Key, out Serf.Message.Members members1)).Select(x => x))
                    {
                        if (!_peers.TryRemove(node.Key, out List<PeerSocket> ws))
                        {
                            _logger.Error($"<<< LocalNode.BootstrapClients >>>: Failed removing {node.Key}");
                        }
                    }
                }
            }
            catch (ArgumentException)
            { }
            catch (Exception ex)
            {
                _logger.Error($"<<< LocalNode.BootstrapClients >>>: {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        public async Task Broadcast(byte[] data, SocketTopicType topicType)
        {
            await Task.Run(() =>
            {
                try
                {
                    var peers = _peers.Select(p => p.Value.FirstOrDefault(x => x.TopicType == topicType));
                    foreach (var peer in peers)
                    {
                        peer.Socket.Compression = CompressionMethod.Deflate;
                        peer.Socket.Connect();
                        peer.Socket.Send(data);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"<<< LocalNode.Broadcast >>>: {ex}");
                }
            });
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task Close()
        {
            await Task.Run(() =>
            {
                try
                {
                    foreach (var peer in _peers)
                    {
                        peer.Value.ForEach(p =>
                        {
                            p.Socket.CloseAsync();
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"<<< LocalNode.Broadcast >>>: {ex}");
                }
            });
        }
    }
}
