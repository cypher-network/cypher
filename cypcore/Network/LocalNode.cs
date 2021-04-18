// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Dawn;
using Serilog;
using CYPCore.Extensions;
using CYPCore.Serf;
using CYPCore.Models;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public interface ILocalNode
    {
        Task Broadcast(TopicType topicType, byte[] data);
        Task Broadcast(Peer[] peers, TopicType topicType, byte[] data);
        Task<Dictionary<ulong, Peer>> GetPeers();
        void Ready();
        Task Leave();
        Task JoinSeedNodes();
        Task<ulong[]> Nodes();
        public ISerfClient SerfClient { get; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class LocalNode : ILocalNode
    {
        private readonly ISerfClient _serfClient;
        private readonly NetworkClient _networkClient;
        private readonly ILogger _logger;
        private TcpSession _tcpSession;

        public LocalNode(ISerfClient serfClient, NetworkClient networkClient, ILogger logger)
        {
            _serfClient = serfClient;
            _networkClient = networkClient;
            _logger = logger.ForContext("SourceContext", nameof(LocalNode));
            SerfClient = _serfClient;
        }

        public void Ready()
        {
            _tcpSession = _serfClient.TcpSessionsAddOrUpdate(
                new TcpSession(_serfClient.SerfConfigurationOptions.Listening).Connect(_serfClient
                    .SerfConfigurationOptions.RPC));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="topicType"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public async Task Broadcast(TopicType topicType, byte[] data)
        {
            var peers = await GetPeers();
            await Broadcast(peers.Values.ToArray(), topicType, data);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="peers"></param>
        /// <param name="topicType"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public Task Broadcast(Peer[] peers, TopicType topicType, byte[] data)
        {
            Guard.Argument(data, nameof(data)).NotNull();
            Guard.Argument(peers, nameof(peers)).NotNull();
            _logger.Here().Information("Broadcasting {@TopicType} to nodes {@Nodes}", topicType, peers);
            try
            {
                if (peers.Any())
                {
                    foreach (var peer in peers)
                    {
                        Task.Run(async () => { await _networkClient.SendAsync(data, topicType, peer.Host); })
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while bootstrapping clients");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<ulong[]> Nodes()
        {
            var peers = await GetPeers();
            if (peers == null) return null;
            var totalNodes = (ulong) peers.Count;
            return totalNodes != 0 ? peers.Keys.ToArray() : null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<Dictionary<ulong, Peer>> GetPeers()
        {
            var peers = new Dictionary<ulong, Peer>();
            if (_tcpSession == null)
            {
                Ready();
            }

            var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
            _ = _serfClient.Connect(tcpSession.SessionId);
            if (!tcpSession.Ready)
            {
                _logger.Here().Error("Serf client failed to connect");
                return null;
            }

            var membersResult = await _serfClient.Members(tcpSession.SessionId);
            var members = membersResult.Value.Members.ToList();
            foreach (var member in members.Where(member => _serfClient.Name != member.Name && member.Status == "alive"))
            {
                if (_serfClient.ClientId == Helper.Util.HashToId(member.Tags["pubkey"])) continue;
                member.Tags.TryGetValue("rest", out var restEndpoint);
                if (string.IsNullOrEmpty(restEndpoint)) continue;
                if (!Uri.TryCreate($"{restEndpoint}", UriKind.Absolute, out var uri)) continue;
                if (uri.Host is "0.0.0.0" or "::0")
                {
                    continue;
                }

                var peer = new Peer
                {
                    Host = uri.OriginalString,
                    ClientId = Helper.Util.HashToId(member.Tags["pubkey"]),
                    PublicKey = member.Tags["pubkey"],
                    NodeName = member.Name
                };
                if (peers.ContainsKey(peer.ClientId)) continue;
                if (peers.TryAdd(peer.ClientId, peer)) continue;
                _logger.Here().Error("Failed adding or exists in remote nodes: {@Node}", member.Name);
            }

            return peers;
        }

        public ISerfClient SerfClient { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Leave()
        {
            if (_tcpSession == null)
            {
                Ready();
            }

            var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
            _ = _serfClient.Connect(tcpSession.SessionId);
            if (!tcpSession.Ready)
            {
                _logger.Here().Error("Serf client failed to connect");
                return;
            }

            var leaveResult = await _serfClient.Leave(tcpSession.SessionId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task JoinSeedNodes()
        {
            if (_tcpSession == null)
            {
                Ready();
            }

            var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
            _ = _serfClient.Connect(tcpSession.SessionId);
            if (!tcpSession.Ready)
            {
                _logger.Here().Error("Serf client failed to connect");
                return;
            }

            var seedNodes = new SeedNode(_serfClient.SeedNodes.Seeds.Select(x => x));
            var joinResult = await _serfClient.Join(seedNodes.Seeds, tcpSession.SessionId);
        }
    }
}