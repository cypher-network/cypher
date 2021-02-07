// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;

namespace CYPCore.Models
{
    [ProtoContract]
    public class PayloadProto
    {
        [ProtoMember(1)]
        public ulong Node { get; set; }
        [ProtoMember(2)]
        public byte[] Data { get; set; }
        [ProtoMember(3)]
        public byte[] PublicKey { get; set; }
        [ProtoMember(4)]
        public byte[] Signature { get; set; }
        [ProtoMember(5)]
        public string Message { get; set; }
        [ProtoMember(6)]
        public bool Error { get; set; }
    }
}
