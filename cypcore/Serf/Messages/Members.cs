using MessagePack;

using System.Collections.Generic;

namespace CYPCore.Serf.Message
{
    [MessagePackObject]
    public class MembersRequest
    {
    }

    [MessagePackObject]
    public class MembersResponse
    {
        [Key("Members")]
        public IEnumerable<Members> Members { get; set; }
    }

    [MessagePackObject]
    public class Members
    {
        [Key("Name")]
        public string Name { get; set; }

        [Key("Addr")]
        public byte[] Address { get; set; }

        [Key("Port")]
        public int Port { get; set; }

        [Key("Tags")]
        public IDictionary<string, string> Tags { get; set; }

        [Key("Status")]
        public string Status { get; set; }

        [Key("ProtocolMin")]
        public uint ProtocolMin { get; set; }

        [Key("ProtocolMax")]
        public uint ProtocolMax { get; set; }

        [Key("ProtocolCur")]
        public uint ProtocolCur { get; set; }

        [Key("DelegateMin")]
        public uint DelegateMin { get; set; }

        [Key("DelegateMax")]
        public uint DelegateMax { get; set; }

        [Key("DelegateCur")]
        public uint DelegateCur { get; set; }
    }
}
