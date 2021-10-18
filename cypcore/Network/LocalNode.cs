// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CYPCore.Cryptography;
using Dawn;
using Serilog;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using MessagePack;
using Microsoft.Extensions.Options;
using NetMQ;
using NetMQ.Sockets;
using Proto;
using Proto.DependencyInjection;
using MemberState = CYPCore.GossipMesh.MemberState;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public class LocalNode: IActor
    {
        private const int SocketTryWaitTimeoutMilliseconds = 5000;
        
        private readonly ActorSystem _actorSystem;
        private readonly PID _pidCryptoKeySign;
        private readonly IGossipMemberStore _gossipMemberStore;
        private readonly ILogger _logger;
        private readonly IOptions<AppOptions> _options;
        private readonly MemStore<Peer> _peerMemStore = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="gossipMemberStore"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public LocalNode(ActorSystem actorSystem, IGossipMemberStore gossipMemberStore,
            IOptions<AppOptions> options, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidCryptoKeySign = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<CryptoKeySign>());
            _gossipMemberStore = gossipMemberStore;
            _options = options;
            _logger = logger.ForContext("SourceContext", nameof(LocalNode));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task ReceiveAsync(IContext context) => context.Message switch
        {
            LocalNodeDetailsRequest => OnGetDetails(context),
            GossipGraphRequest => OnGetGossipGraph(context),
            BroadcastAutoRequest broadcastRequest => OnBroadcast(broadcastRequest, context),
            BroadcastManualRequest broadcastManualRequest => OnBroadcast(broadcastManualRequest),
            PeersMemStoreRequest peersMemStoreRequest => OnTryGetPeers(peersMemStoreRequest, context),
            _ => Task.CompletedTask
        };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task OnGetDetails(IContext context)
        {
            Guard.Argument(context, nameof(context)).NotNull();
            var keyPairResponse = await _actorSystem.Root.RequestAsync<KeyPairResponse>(_pidCryptoKeySign,
                new KeyPairRequest(CryptoKeySign.DefaultSigningKeyName));
            context.Respond(new LocalNodeDetailsResponse(
                Util.ToHashIdentifier(keyPairResponse.KeyPair.PublicKey.ByteToHex()), _options.Value.Name,
                _options.Value.RestApi, _options.Value.Gossip.Listening, Util.GetAssemblyVersion()));
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private Task OnGetGossipGraph(IContext context)
        {
            Guard.Argument(context, nameof(context)).NotNull();
            context.Respond(new GossipGraphResponse( _gossipMemberStore.GetGraph()));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="broadcastRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task OnBroadcast(BroadcastAutoRequest broadcastRequest, IContext context)
        {
            Guard.Argument(broadcastRequest, nameof(broadcastRequest)).NotNull();

            var props = Props(_actorSystem, _gossipMemberStore, _options, _logger);
            var self = context.Spawn(props);
            var peersMemStoreResponse =
                await context.RequestAsync<PeersMemStoreResponse>(self, new PeersMemStoreRequest());
            var snapshot = peersMemStoreResponse.MemStore.GetMemSnapshot().SnapshotAsync();
            var peers = await snapshot.ToArrayAsync();
            if (peers.Any())
            {
                await OnBroadcast(new BroadcastManualRequest(peers.Select(x => x.Value).ToArray(),
                    broadcastRequest.TopicType, broadcastRequest.Data));
                return;
            }

            _logger.Here().Fatal("No peers found for Broadcast");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="broadcastManualRequest"></param>
        /// <returns></returns>
        private Task OnBroadcast(BroadcastManualRequest broadcastManualRequest)
        {
            Guard.Argument(broadcastManualRequest, nameof(broadcastManualRequest)).NotNull();
            _logger.Here().Information("Broadcasting {@TopicType} to nodes {@Nodes}", broadcastManualRequest.TopicType, broadcastManualRequest.Peers);
            try
            {
                if (broadcastManualRequest.Peers.Any())
                {
                    var tasks = new List<Task>();
                    foreach (var peer in broadcastManualRequest.Peers)
                    {
                        if (peer is null) continue;

                        void Action()
                        {
                            var command = broadcastManualRequest.TopicType switch
                            {
                                TopicType.AddTransaction => CommandMessage.Transaction,
                                _ => CommandMessage.BlockGraph
                            };
                            using var dealerSocket = new DealerSocket($">tcp://{peer.Listening}");
                            dealerSocket.Options.Identity = Util.RandomDealerIdentity();
                            var message = new NetMQMessage();
                            message.Append(command.ToString());
                            message.Append(MessagePackSerializer.Serialize(new[] { new Parameter { Value = broadcastManualRequest.Data } }));
                            dealerSocket.SendMultipartMessage(message);
                            if (dealerSocket.TryReceiveFrameString(
                                TimeSpan.FromMilliseconds(SocketTryWaitTimeoutMilliseconds), out var msg))
                            {
                                if (command == CommandMessage.Transaction)
                                {
                                    var newTransactionResponse =
                                        MessagePackSerializer.Deserialize<NewTransactionResponse>(msg.HexToByte());
                                    if (!newTransactionResponse.OK)
                                    {
                                        _logger.Here().Error("Unable to forward new transaction to {@peer}",
                                            peer.Listening);
                                    }
                                }
                                else if (command == CommandMessage.BlockGraph)
                                {
                                    var newBlockGraphResponse =
                                        MessagePackSerializer.Deserialize<NewBlockGraphResponse>(msg.HexToByte());
                                    if (!newBlockGraphResponse.OK)
                                    {
                                        _logger.Here().Error("Unable to forward new blockgraph to {@host}",
                                            peer.Listening);
                                    }
                                }
                            }
                            else
                            {
                                _logger.Here().Error("Dead peer {@peer}", peer.Listening);
                            }
                        }

                        var t = new Task(Action);
                        t.Start();
                        tasks.Add(t);
                    }

                    Task.WaitAll(tasks.ToArray());
                }
                else
                {
                    _logger.Here().Fatal("Broadcast failed. No peers");
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error broadcast");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="peersMemStoreRequest"></param>
        /// <param name="context"></param>
        private async Task OnTryGetPeers(PeersMemStoreRequest peersMemStoreRequest, IContext context)
        {
            Guard.Argument(peersMemStoreRequest, nameof(peersMemStoreRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            try
            {
                var tasks = new List<Task>();
                foreach (var node in _gossipMemberStore.GetGraph().Nodes)
                {
                    async void Action() => await GetPeer(node, peersMemStoreRequest.ShouldUpdateHeight);
                    var t = new Task(Action);
                    t.Start();
                    tasks.Add(t);
                }

                await Task.WhenAll(tasks.ToArray());
                context.Respond(new PeersMemStoreResponse(_peerMemStore));
                return;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }
            
            context.Respond(new PeersMemStoreResponse(null));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="updateHeight"></param>
        private Task GetPeer(GossipGraph.Node node, bool updateHeight)
        {
            Guard.Argument(node, nameof(node)).NotNull();
            if (node.State is MemberState.Dead or MemberState.Left or MemberState.Pruned)
            {
                if (!_peerMemStore.TryGet(node.Id.GetHashCode().ToBytes(), out _)) return Task.CompletedTask;
                _peerMemStore.Delete(node.Id.GetHashCode().ToBytes());
                return Task.CompletedTask;
            }
            
            try
            {
                if (_peerMemStore.Contains(node.Id.GetHashCode().ToBytes()) && updateHeight)
                {
                    var value = GetPeerOrHeight(node, CommandMessage.GetBlockCount);
                    if (!_peerMemStore.TryGet(node.Id.GetHashCode().ToBytes(), out var peer)) return Task.CompletedTask;
                    if (string.IsNullOrEmpty(value)) return Task.CompletedTask;
                    var blockCountResponse = MessagePackSerializer.Deserialize<BlockCountResponse>(value.HexToByte());
                    if (blockCountResponse is { })
                    {
                        peer.BlockHeight = (ulong)blockCountResponse.Count;
                    }
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error getting peer height information {@Host}", node.Ip.ToString());
            }

            try
            {
                if (_peerMemStore.Contains(node.Id.GetHashCode().ToBytes())) return Task.CompletedTask;
                var value = GetPeerOrHeight(node, CommandMessage.GetPeer);
                if (string.IsNullOrEmpty(value)) return Task.CompletedTask;
                var peerResponse = MessagePackSerializer.Deserialize<PeerResponse>(value.HexToByte());
                if (peerResponse is { })
                {
                    _peerMemStore.Put(node.Id.GetHashCode().ToBytes(), peerResponse.Peer);
                    return Task.CompletedTask;
                }
                
                _logger.Here().Error("Dead peer {@peer}", node.Ip.ToString());
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error getting peer information {@Host}", node.Ip.ToString());
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        private string GetPeerOrHeight(GossipGraph.Node node, CommandMessage command)
        {
            Guard.Argument(node, nameof(node)).NotNull();
            Guard.Argument(command, nameof(command)).In(CommandMessage.GetBlockCount, CommandMessage.GetBlockHeight, CommandMessage.GetPeer);
            try
            {
                using var dealerSocket = new DealerSocket($">tcp://{node.Id}");
                dealerSocket.Options.Identity = Util.RandomDealerIdentity();
                dealerSocket.SendFrame(command.ToString());
                if (dealerSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(SocketTryWaitTimeoutMilliseconds), out var msg))
                {
                    return msg;
                }
                
                _logger.Here().Warning("Dead message {@peer}", node.Ip.ToString());
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return string.Empty;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="gossipMemberStore"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public  static Props Props(ActorSystem actorSystem, IGossipMemberStore gossipMemberStore,
            IOptions<AppOptions> options, ILogger logger)
        {
            var props = Proto.Props.FromProducer(() => new LocalNode(actorSystem, gossipMemberStore, options, logger));
            return props;
        }
    }
}