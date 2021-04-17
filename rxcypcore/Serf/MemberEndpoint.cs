using System.Linq;
using System.Net;
using Autofac.Core;
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

            if (!member.Tags.TryGetValue("IPv", out var ipvText)) return;
            if (!ushort.TryParse(ipvText, out var ipv)) return;
            IPv = ipv;

            if (!member.Tags.TryGetValue("APIPort", out var apiPortText)) return;
            if (!ushort.TryParse(apiPortText, out var apiPort)) return;
            APIPort = apiPort;
        }

        [Key("IPAddress")]
        public IPAddress Address { get; set; }

        [Key("Port")]
        public ushort Port { get; set; }

        [Key("APIPort")]
        public ushort? APIPort { get; set; } = null;

        [Key("IPv")]
        public ushort IPv { get; set; }

        [Key("Status")]
        public string Status { get; set; }

        public IPEndPoint GetEndPoint()
        {
            return new(Address, Port);
        }

        public IPEndPoint GetAPIEndPoint()
        {
            if (APIPort != null)
            {
                return new(Address, (ushort)APIPort);
            }

            return null;
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