// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;

namespace CYPCore.Models
{
    [ProtoContract]
    public class VoutProto
    {
        [ProtoMember(1)]
        public ulong A { get; set; }
        [ProtoMember(2)]
        public CoinType T { get; set; }
        [ProtoMember(3)]
        public byte[] C { get; set; }
        [ProtoMember(4)]
        public string E { get; set; }
        [ProtoMember(5)]
        public byte[] N { get; set; }
        [ProtoMember(6)]
        public string P { get; set; }
        [ProtoMember(7)]
        public long UNLK { get; set; }
        [ProtoMember(8)]
        public string Scr { get; set; }
        [ProtoMember(9)]
        public byte[] Sig { get; set; }
    }
}
