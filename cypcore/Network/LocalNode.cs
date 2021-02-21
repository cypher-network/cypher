// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;

using Dawn;
using Serilog;

using CYPCore.Extensions;
using CYPCore.Serf;
using CYPCore.Models;
using CYPCore.Services.Rest;

namespace CYPCore.Network
{
    public class LocalNode : ILocalNode
    {
        private readonly ISerfClient _serfClient;
        private readonly ILogger _logger;
        private TcpSession _tcpSession;

        public LocalNode(ISerfClient serfClient, ILogger logger)
        {
            _serfClient = serfClient;
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
                var tasks = new  List<Task>();

                var peers = await GetPeers();
                if (peers == null) return;
                
                var broadcastPeers = peers.Select(p => p).ToList();
                broadcastPeers.ForEach(x => { tasks.Add(Send(data, topicType, x.Value.Host)); });

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
        public async Task<Dictionary<ulong, Peer>> GetPeers()
        {
            var peers = new Dictionary<ulong, Peer>();
            
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
                member.Tags.TryGetValue("rest", out var restEndpoint);

                if (string.IsNullOrEmpty(restEndpoint)) continue;
                if (!Uri.TryCreate($"{restEndpoint}", UriKind.Absolute, out var uri)) continue;
                if (peers.TryAdd(Helper.Util.HashToId(member.Tags["pubkey"]), new Peer {Host = uri.OriginalString})) continue;
                    
                _logger.Here().Error("Failed adding or exists in remote nodes: {@Node}",
                    member.Name);
                
                return null;
            }

            return peers;
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

            try
            {
                if (Uri.TryCreate($"{host}", UriKind.Absolute, out var uri))
                {
                    switch (topicType)
                    {
                        case TopicType.AddBlock:
                            {
                                RestBlockService restBlockService = new(uri);
                                await restBlockService.AddBlock(data);
                                return;
                            }
                        case TopicType.AddMemoryPool:
                            {
                                RestMemoryPoolService restMemoryPoolService = new(uri);
                                await restMemoryPoolService.AddMemoryPool(data);
                                return;
                            }
                    }
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            catch (Refit.ApiException)
            {
            }
        }
    }
}