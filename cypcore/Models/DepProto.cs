// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using ProtoBuf;

namespace CYPCore.Models
{
    [ProtoContract]
    public class DepProto
    {
        [ProtoMember(1)]
        public InterpretedProto Block = new InterpretedProto();
        [ProtoMember(2)]
        public List<InterpretedProto> Deps = new List<InterpretedProto>();
        [ProtoMember(3)]
        public InterpretedProto Prev = new InterpretedProto();
    }
}
