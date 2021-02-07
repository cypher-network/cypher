// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using CYPCore.Serf;
using CYPCore.Models;
using CYPCore.Services.Rest;

namespace CYPCore.Network
{
    public class LocalNode : ILocalNode
    {
        private readonly ConcurrentDictionary<ulong, Peer> _peers;
        private readonly ISerfClient _serfClient;
        private readonly ILogger _logger;
        private TcpSession _tcpSession;

        public LocalNode(ISerfClient serfClient, ILogger<LocalNode> logger)
        {
            _serfClient = serfClient;
            _logger = logger;
            _peers = new ConcurrentDictionary<ulong, Peer>();
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
                        .Where(member => !_peers.TryGetValue(Helper.Util.HashToId(member.Tags["pubkey"]), out Peer peer)).Select(member => member))
                    {
                        if (_serfClient.Name == member.Name)
                            continue;

                        if (member.Status != "alive")
                            continue;

                        member.Tags.TryGetValue("rest", out string restEndpoint);

                        if (string.IsNullOrEmpty(restEndpoint))
                            continue;

                        if (Uri.TryCreate($"{restEndpoint}", UriKind.Absolute, out Uri uri))
                        {
                            if (!_peers.TryAdd(Helper.Util.HashToId(member.Tags["pubkey"]), new Peer { Host = uri.AbsolutePath }))
                            {
                                _logger.LogError($"<<< LocalNode.Connect >>>: Failed adding or exists in remote nodes: {member.Name}");
                                return;
                            }
                        }
                    }

                    foreach (var node in _peers
                        .Where(node => !membersResult.Value.Members.ToDictionary(x => Helper.Util.HashToId(x.Tags["pubkey"])).TryGetValue(node.Key, out Serf.Message.Members members1)).Select(x => x))
                    {
                        if (!_peers.TryRemove(node.Key, out Peer peer))
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
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task Broadcast(byte[] data, TopicType topicType, string path)
        {
            var peers = _peers.Select(p => p).ToList();
            await Task.Run(() => Parallel.ForEach(peers, async peer => await Send(data, topicType, peer.Value.Host, path)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="topicType"></param>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task Send(byte[] data, TopicType topicType, string host, string path)
        {
            try
            {
                if (Uri.TryCreate($"{host}/{path}", UriKind.Absolute, out Uri uri))
                {
                    if (topicType == TopicType.AddBlock)
                    {
                        await new RestBlockService(uri).AddBlock(data);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< LocalNode.Send >>>: {ex}");
            }
        }
    }
}
