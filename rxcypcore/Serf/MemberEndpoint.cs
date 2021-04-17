using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime;
using Dawn;
using MessagePack;
using rxcypcore.Serf.Messages;

namespace rxcypcore.Serf
{
    [MessagePackObject]
    public class MemberEndpoint
    {
        public MemberEndpoint()
        {
        }

        public MemberEndpoint(Member member)
        {
            Guard.Argument(member, nameof(member)).NotNull();
            Guard.Argument(member.Addr.Length, nameof(member.Addr)).Min(4);
            Guard.Argument(member.Port, nameof(member.Port)).InRange(1, ushort.MaxValue);

            // TODO: Find out why some IPv4 member addresses contain more than 4 bytes
            // TODO: Add IPv6 peers
            var ipv4Address = member.Addr.TakeLast(4).ToArray();
            Address = new IPAddress(ipv4Address);
            Port = (ushort)member.Port;

            Status = member.Status;
        }

        [Key("IPAddress")]
        public IPAddress Address { get; set; }

        [Key("Port")]
        public ushort Port { get; set; }

        [Key("Status")]
        public string Status { get; set; }

        public IPEndPoint GetEndPoint()
        {
            return new(Address, Port);
        }

        public override bool Equals(object? obj)
        {
            return obj is MemberEndpoint other && other.GetEndPoint().Equals(GetEndPoint());
        }

        public override int GetHashCode()
        {
            return GetEndPoint().GetHashCode();
        }
    }
}