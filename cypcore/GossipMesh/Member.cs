using System.IO;
using System.Net;
using System.Threading;

namespace CYPCore.GossipMesh
{
    /// <summary>
    /// 
    /// </summary>
    public class Member
    {
        private long _gossipCounter = 0;

        public MemberState State { get; private set; }
        public IPAddress IP { get; private set; }
        public ushort GossipPort { get; private set; }
        public byte Generation { get; internal set; }
        public byte Service { get; private set; }
        public ushort ServicePort { get; private set; }
        internal long GossipCounter => Interlocked.Read(ref _gossipCounter);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        internal Member(MemberEvent memberEvent)
        {
            IP = memberEvent.IP;
            GossipPort = memberEvent.GossipPort;
            State = memberEvent.State;
            Generation = memberEvent.Generation;
            Service = memberEvent.Service;
            ServicePort = memberEvent.ServicePort;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="state"></param>
        /// <param name="ip"></param>
        /// <param name="gossipPort"></param>
        /// <param name="generation"></param>
        /// <param name="service"></param>
        /// <param name="servicePort"></param>
        public Member(MemberState state, IPAddress ip, ushort gossipPort, byte generation, byte service, ushort servicePort)
        {
            State = state;
            IP = ip;
            GossipPort = gossipPort;
            Generation = generation;
            Service = service;
            ServicePort = servicePort;
        }

        /// <summary>
        /// 
        /// </summary>
        internal IPEndPoint GossipEndPoint => new(IP, GossipPort);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        internal void Update(MemberEvent memberEvent)
        {
            State = memberEvent.State;
            Generation = memberEvent.Generation;

            if (memberEvent.State == MemberState.Alive)
            {
                Service = memberEvent.Service;
                ServicePort = memberEvent.ServicePort;
            }

            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="state"></param>
        internal void Update(MemberState state)
        {
            State = state;
            Interlocked.Exchange(ref _gossipCounter, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newGeneration"></param>
        /// <returns></returns>
        internal bool IsLaterGeneration(byte newGeneration)
        {
            return 0 < newGeneration - Generation && newGeneration - Generation < 191 ||
                   newGeneration - Generation <= -191;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newState"></param>
        /// <returns></returns>
        internal bool IsStateSuperseded(MemberState newState)
        {
            // alive < suspicious < dead < left
            return State < newState;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        internal void WriteTo(Stream stream)
        {
            stream.WriteIPEndPoint(GossipEndPoint);
            stream.WriteByte((byte)State);
            stream.WriteByte(Generation);

            if (State == MemberState.Alive)
            {
                stream.WriteByte(Service);
                stream.WritePort(ServicePort);
            }

            Interlocked.Increment(ref _gossipCounter);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return
                $"IP:{IP} GossipPort:{GossipPort} State:{State} Generation:{Generation} Service:{Service} ServicePort:{ServicePort}";
        }
    }
}