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
        private readonly ConcurrentDictionary<ulong, Peer> _peers;
        private readonly ISerfClient _serfClient;
        private readonly ILogger _logger;
        private TcpSession _tcpSession;

        public LocalNode(ISerfClient serfClient, ILogger logger)
        {
            _serfClient = serfClient;
            _logger = logger.ForContext("SourceContext", nameof(LocalNode));
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
                var membersResult = await _serfClient.Members(tcpSession.SessionId);

                if (tcpSession.Ready)
                {
                    _ = _serfClient.Connect(tcpSession.SessionId);
                    if (!membersResult.Success)
                    {
                        return;
                    }

                    foreach (var member in membersResult.Value.Members
                        .Where(member =>
                            !_peers.TryGetValue(Helper.Util.HashToId(member.Tags["pubkey"]), out Peer peer))
                        .Select(member => member))
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
                            if (!_peers.TryAdd(Helper.Util.HashToId(member.Tags["pubkey"]),
                                new Peer { Host = uri.OriginalString }))
                            {
                                _logger.Here().Error("Failed adding or exists in remote nodes: {@Node}",
                                    member.Name);

                                return;
                            }
                        }
                    }

                    foreach (var node in _peers
                        .Where(node =>
                            !membersResult.Value.Members.ToDictionary(x => Helper.Util.HashToId(x.Tags["pubkey"]))
                                .TryGetValue(node.Key, out Serf.Message.Members members1)).Select(x => x))
                    {
                        if (!_peers.TryRemove(node.Key, out Peer peer))
                        {
                            _logger.Here().Error("Failed removing {@Node}",
                                node.Key);

                        }
                    }
                }
            }
            catch (ArgumentException)
            {
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while bootstrapping clients");
            }
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

            List<Task> tasks = new();
            var peers = _peers.Select(p => p).ToList();

            peers.ForEach(x => { tasks.Add(Send(data, topicType, x.Value.Host)); });

            await Task.WhenAll(tasks);
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