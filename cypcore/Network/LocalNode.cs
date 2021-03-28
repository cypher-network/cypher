// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using System.Reactive.Linq;
using Dawn;
using Serilog;
using CYPCore.Extensions;
using CYPCore.Serf;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Services.Rest;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public interface ILocalNode
    {
        Task Broadcast(byte[] data, TopicType topicType);
        Task Broadcast(byte[] data, Peer[] peers, TopicType topicType);
        Task Send(byte[] data, TopicType topicType, string host);
        Task<Dictionary<ulong, Peer>> GetPeers();
        void Ready();
        Task<NetworkBlockHeight> PeerBlockHeight(Peer peer);
        Task Leave();
        Task JoinSeedNodes();
        IObservable<Peer> ObservePeers();
        Task<ulong[]> Nodes();
    }

    /// <summary>
    /// 
    /// </summary>
    public class LocalNode : ILocalNode
    {
        private readonly ISerfClient _serfClient;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _logger;
        private TcpSession _tcpSession;

        public LocalNode(ISerfClient serfClient, IUnitOfWork unitOfWork, ILogger logger)
        {
            _serfClient = serfClient;
            _unitOfWork = unitOfWork;
            _logger = logger.ForContext("SourceContext", nameof(LocalNode));
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
        /// <param name="data"></param>
        /// <param name="topicType"></param>
        /// <returns></returns>
        public async Task Broadcast(byte[] data, TopicType topicType)
        {
            Guard.Argument(data, nameof(data)).NotNull();

            try
            {
                var tasks = new List<Task>();

                var peers = await GetPeers();
                if (peers == null) return;

                var broadcastPeers = peers.Select(p => p).ToList();
                broadcastPeers.ForEach(x => { tasks.Add(Send(data, topicType, x.Value.Host)); });

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while broadcasting to clients");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="peers"></param>
        /// <param name="topicType"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task Broadcast(byte[] data, Peer[] peers, TopicType topicType)
        {
            Guard.Argument(data, nameof(data)).NotNull();
            Guard.Argument(peers, nameof(peers)).NotNull();

            _logger.Here().Debug("Broadcasting {@TopicType} to nodes {@Nodes}", topicType, peers);

            var tasks = new List<Task>();

            try
            {
                var broadcastPeers = peers.Select(p => p).ToList();
                broadcastPeers.ForEach(peer => { tasks.Add(Send(data, topicType, peer.Host)); });

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while bootstrapping clients");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IObservable<Peer> ObservePeers()
        {
            return Observable.Defer(async () =>
            {
                var peers = await GetPeers();
                return peers.Values.ToObservable();
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<ulong[]> Nodes()
        {
            var peers = await GetPeers();
            if (peers == null) return null;

            var totalNodes = (ulong)peers.Count;
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

            foreach (var member in members.Where(member =>
                _serfClient.Name != member.Name && member.Status == "alive"))
            {
                if (_serfClient.ClientId == Helper.Util.HashToId(member.Tags["pubkey"])) continue;

                member.Tags.TryGetValue("rest", out var restEndpoint);

                if (string.IsNullOrEmpty(restEndpoint)) continue;

                if (!Uri.TryCreate($"{restEndpoint}", UriKind.Absolute, out var uri)) continue;

                var peer = new Peer
                {
                    Host = uri.OriginalString,
                    ClientId = Helper.Util.HashToId(member.Tags["pubkey"]),
                    PublicKey = member.Tags["pubkey"],
                    NodeName = member.Name
                };

                if (peers.ContainsKey(peer.ClientId)) continue;
                if (peers.TryAdd(peer.ClientId, peer)) continue;

                _logger.Here().Error("Failed adding or exists in remote nodes: {@Node}",
                    member.Name);
            }

            return peers;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="peer"></param>
        /// <returns></returns>
        public async Task<NetworkBlockHeight> PeerBlockHeight(Peer peer)
        {
            var uri = new Uri(peer.Host);
            var blockRestApi = new RestBlockService(uri, _logger);
            var networkBlockHeight = new NetworkBlockHeight
            {
                Local = new BlockHeight { Height = await _unitOfWork.DeliveredRepository.CountAsync() },
                Remote = await blockRestApi.GetHeight()
            };

            return networkBlockHeight;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="topicType"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        public async Task Send(byte[] data, TopicType topicType, string host)
        {
            Guard.Argument(data, nameof(data)).NotNull();
            Guard.Argument(host, nameof(data)).NotNull().NotEmpty().NotWhiteSpace();

            _logger.Here().Debug("Sending {@TopicType} to {@Node}", topicType, host);

            try
            {
                if (Uri.TryCreate($"{host}", UriKind.Absolute, out var uri))
                {
                    switch (topicType)
                    {
                        case TopicType.AddBlock:
                            {
                                RestBlockService restBlockService = new(uri, _logger);
                                await restBlockService.AddBlock(data);
                                return;
                            }
                        case TopicType.AddBlockGraph:
                            {
                                BlockGraphRestService blockGraphRestService = new(uri, _logger);
                                await blockGraphRestService.AddBlockGraph(data);
                                return;
                            }
                        case TopicType.AddTransaction:
                            {
                                TransactionRestService restTransactionService = new(uri, _logger);
                                await restTransactionService.AddTransaction(data);
                                return;
                            }
                    }
                }
                else
                {
                    _logger.Here().Error("Cannot create URI for host {@Host}", host);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.Here().Error(ex, "HttpRequestException for {@Host}", host);
            }
            catch (TaskCanceledException ex)
            {
                _logger.Here().Error(ex, "TaskCanceledException for {@Host}", host);
            }
            catch (Refit.ApiException ex)
            {
                _logger.Here().Error(ex, "ApiException for {@Host}", host);
            }
        }

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