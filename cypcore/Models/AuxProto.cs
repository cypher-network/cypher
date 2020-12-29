// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;

namespace CYPCore.Models
{
    [ProtoContract]
    public class AuxProto
    {
        [ProtoMember(1)]
        public byte[] K_Image { get; set; }
        [ProtoMember(2)]
        public byte[] K_Offsets { get; set; }
    }
}
