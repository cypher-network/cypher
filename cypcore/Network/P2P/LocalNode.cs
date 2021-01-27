// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

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

        public LocalNode(ISerfClient serfClient, ILogger<LocalNode> logger)
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
                        int port;

                        port = Convert.ToInt32(member.Tags["p2pblockport"]);
                        peerSockets.Add(new PeerSocket { WSAddress = $"ws://{address}:{port}/{SocketTopicType.Block}", TopicType = SocketTopicType.Block });

                        port = Convert.ToInt32(member.Tags["p2pmempoolport"]);
                        peerSockets.Add(new PeerSocket { WSAddress = $"ws://{address}:{port}/{SocketTopicType.Mempool}", TopicType = SocketTopicType.Mempool });

                        if (!_peers.TryAdd(Helper.Util.HashToId(member.Tags["pubkey"]), peerSockets))
                        {
                            _logger.LogError($"<<< LocalNode.Connect >>>: Failed adding or exists in remote nodes: {member.Name}");
                            return;
                        }
                    }

                    foreach (var node in _peers
                        .Where(node => !membersResult.Value.Members.ToDictionary(x => Helper.Util.HashToId(x.Tags["pubkey"])).TryGetValue(node.Key, out Serf.Message.Members members1)).Select(x => x))
                    {
                        if (!_peers.TryRemove(node.Key, out List<PeerSocket> ws))
                        {
                            _logger.LogError($"<<< LocalNode.BootstrapClients >>>: Failed removing {node.Key}");
                        }
                    }
                }
            }
            catch (ArgumentException)
            { }
            catch (Exception ex)
            {
                _logger.LogError($"<<< LocalNode.BootstrapClients >>>: {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="topicType"></param>
        /// <returns></returns>
        public async Task Broadcast(byte[] data, SocketTopicType topicType)
        {
            var peers = _peers.Select(p => p.Value.FirstOrDefault(x => x.TopicType == topicType)).ToList();
            await Task.Run(() => Parallel.ForEach(peers, peer => Send(data, peer.WSAddress)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="peer"></param>
        /// <returns></returns>
        public Task Send(byte[] data, string address)
        {
            try
            {
                using var ws = new WebSocket(address)
                {
                    Compression = CompressionMethod.Deflate
                };
                ws.Connect();
                ws.Send(data);
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< LocalNode.Send >>>: {ex}");
            }

            return Task.CompletedTask;
        }
    }
}
