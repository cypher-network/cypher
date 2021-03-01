// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;

namespace CYPCore.Models
{
    [ProtoContract]
    public class RCTProto
    {
        [ProtoMember(1)] public byte[] M { get; set; }
        [ProtoMember(2)] public byte[] P { get; set; }
        [ProtoMember(3)] public byte[] S { get; set; }
        [ProtoMember(4)] public byte[] I { get; set; }
    }
}
