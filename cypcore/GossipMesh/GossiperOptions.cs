using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace CYPCore.GossipMesh
{
    public class GossiperOptions
    {
        public int MaxUdpPacketBytes { get; set; } = 508;
        private int _protocolPeriodMilliseconds = 500;
        private int _ackTimeoutMilliseconds = 250;
        private int _deadTimeoutMilliseconds = 5000;
        public int ProtocolPeriodMilliseconds
        {
            get => _protocolPeriodMilliseconds;
            set
            {
                _protocolPeriodMilliseconds = value;
                _ackTimeoutMilliseconds = value / 2;
                _deadTimeoutMilliseconds = value * 10;
            }
        }
        public int AckTimeoutMilliseconds => _ackTimeoutMilliseconds;
        public int DeadTimeoutMilliseconds => _deadTimeoutMilliseconds;
        public int DeadCoolOffMilliseconds { get; set; } = 300000;
        public int PruneTimeoutMilliseconds { get; set; } = 600000;
        public int FanoutFactor { get; set; } = 3;
        public int NumberOfIndirectEndpoints { get; set; } = 3;
        public IPEndPoint[] SeedMembers { get; set; } = Array.Empty<IPEndPoint>();
        public IEnumerable<IMemberListener> MemberListeners { get; set; } = Enumerable.Empty<IMemberListener>();
    }
}