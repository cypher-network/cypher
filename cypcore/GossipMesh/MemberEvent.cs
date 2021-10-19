using System;
using System.IO;
using System.Net;

namespace CYPCore.GossipMesh
{
    /// <summary>
    /// 
    /// </summary>
    public class MemberEvent
    {
        public IPEndPoint SenderGossipEndPoint;
        public DateTime ReceivedDateTime;

        public MemberState State { get; private set; }
        public IPAddress IP { get; private set; }
        public ushort GossipPort { get; private set; }
        public byte Generation { get; private set; }
        public byte Service { get; private set; }
        public ushort ServicePort { get; private set; }

        public IPEndPoint GossipEndPoint => new(IP, GossipPort);

        private MemberEvent()
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="senderGossipEndPoint"></param>
        /// <param name="receivedDateTime"></param>
        /// <param name="ip"></param>
        /// <param name="gossipPort"></param>
        /// <param name="state"></param>
        /// <param name="generation"></param>
        internal MemberEvent(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, IPAddress ip, ushort gossipPort, MemberState state, byte generation)
        {
            SenderGossipEndPoint = senderGossipEndPoint;
            ReceivedDateTime = receivedDateTime;

            IP = ip;
            GossipPort = gossipPort;
            State = state;
            Generation = generation;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="senderGossipEndPoint"></param>
        /// <param name="receivedDateTime"></param>
        /// <param name="member"></param>
        internal MemberEvent(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Member member)
        {
            SenderGossipEndPoint = senderGossipEndPoint;
            ReceivedDateTime = receivedDateTime;

            IP = member.IP;
            GossipPort = member.GossipPort;
            State = member.State;
            Generation = member.Generation;
            Service = member.Service;
            ServicePort = member.ServicePort;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="senderGossipEndPoint"></param>
        /// <param name="receivedDateTime"></param>
        /// <param name="stream"></param>
        /// <param name="isSender"></param>
        /// <returns></returns>
        internal static MemberEvent ReadFrom(IPEndPoint senderGossipEndPoint, DateTime receivedDateTime, Stream stream, bool isSender = false)
        {
            if (stream.Position >= stream.Length)
            {
                return null;
            }

            var memberEvent = new MemberEvent
            {
                SenderGossipEndPoint = senderGossipEndPoint,
                ReceivedDateTime = receivedDateTime,

                IP = isSender ? senderGossipEndPoint.Address : stream.ReadIPAddress(),
                GossipPort = isSender ? (ushort)senderGossipEndPoint.Port : stream.ReadPort(),
                State = isSender ? MemberState.Alive : stream.ReadMemberState(),
                Generation = (byte)stream.ReadByte(),
            };

            if (memberEvent.State != MemberState.Alive) return memberEvent;
            memberEvent.Service = (byte)stream.ReadByte();
            memberEvent.ServicePort = stream.ReadPort();

            return memberEvent;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return
                $"Sender:{SenderGossipEndPoint} Received:{ReceivedDateTime} IP:{IP} GossipPort:{GossipPort} State:{State} Generation:{Generation} Service:{Service} ServicePort:{ServicePort}";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        /// <returns></returns>
        public bool Equal(MemberEvent memberEvent)
        {
            return memberEvent != null &&
                    IP.Equals(memberEvent.IP) &&
                    GossipPort == memberEvent.GossipPort &&
                    State == memberEvent.State &&
                    Generation == memberEvent.Generation &&
                    Service == memberEvent.Service &&
                    ServicePort == memberEvent.ServicePort;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        /// <returns></returns>
        public bool NotEqual(MemberEvent memberEvent)
        {
            return !Equal(memberEvent);
        }
    }
}