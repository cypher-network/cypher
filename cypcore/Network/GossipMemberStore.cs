using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using CYPCore.GossipMesh;
using Dawn;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public interface IGossipMemberStore
    {
        GossipGraph.Node AddOrUpdateNode(MemberEvent memberEvent);
        GossipGraph GetGraph();
    }
    
    /// <summary>
    /// 
    /// </summary>
    public class GossipMemberStore : IGossipMemberStore
    {
        private readonly object _memberGraphLocker = new();
        private readonly Dictionary<IPEndPoint, GossipGraph.Node> _nodes = new();
        private readonly Random _random = new();
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        /// <returns></returns>
        public GossipGraph.Node AddOrUpdateNode(MemberEvent memberEvent)
        {
            Guard.Argument(memberEvent, nameof(memberEvent)).NotNull();
            lock (_memberGraphLocker)
            {
                if (!_nodes.TryGetValue(memberEvent.GossipEndPoint, out var node))
                {
                    node = new GossipGraph.Node
                    {
                        Id = memberEvent.GossipEndPoint,
                        Ip = memberEvent.IP,
                        State = memberEvent.State,
                        Generation = memberEvent.Generation,
                        Service = memberEvent.Service,
                        ServicePort = memberEvent.ServicePort,
                        X = (byte)_random.Next(0, 255),
                        Y = (byte)_random.Next(0, 255)
                    };
                    _nodes.Add(memberEvent.GossipEndPoint, node);
                }
                else if (memberEvent.State == MemberState.Alive)
                {
                    node = new GossipGraph.Node
                    {
                        Id = memberEvent.GossipEndPoint,
                        Ip = memberEvent.IP,
                        State = memberEvent.State,
                        Generation = memberEvent.Generation,
                        Service = memberEvent.Service,
                        ServicePort = memberEvent.ServicePort,
                        X = node.X,
                        Y = node.Y
                    };
                    _nodes[memberEvent.GossipEndPoint] = node;
                }
                else
                {
                    node = new GossipGraph.Node
                    {
                        Id = memberEvent.GossipEndPoint,
                        Ip = memberEvent.IP,
                        State = memberEvent.State,
                        Generation = memberEvent.Generation,
                        Service = node.Service,
                        ServicePort = node.ServicePort,
                        X = node.X,
                        Y = node.Y
                    };
                    if (memberEvent.State == MemberState.Pruned)
                    {
                        _nodes.Remove(memberEvent.GossipEndPoint);
                    }
                    else
                    {
                        _nodes[memberEvent.GossipEndPoint] = node;
                    }
                }

                return node;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public GossipGraph GetGraph()
        {
            lock (_memberGraphLocker)
            {
                return new GossipGraph { Nodes = _nodes.Values.ToArray() };
            }
        }
    }
}