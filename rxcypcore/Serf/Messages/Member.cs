using System.Collections.Generic;
using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject(true)]
    public class MembersResponse
    {
        public IEnumerable<Member> Members { get; set; }
    }

    [MessagePackObject(true)]
    public class Member
    {
        public string Name { get; set; }

        public byte[] Addr { get; set; }
        public int Port { get; set; }

        public IDictionary<string, string> Tags { get; set; }
        public string Status { get; set; }

        public uint ProtocolMin { get; set; }
        public uint ProtocolMax { get; set; }
        public uint ProtocolCur { get; set; }

        public uint DelegateMin { get; set; }
        public uint DelegateMax { get; set; }
        public uint DelegateCur { get; set; }
    }
}