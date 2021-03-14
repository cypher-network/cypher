// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class DepProto : object
    {
        [FlatBufferItem(0)] public virtual InterpretedProto Block { get; set; }
        [FlatBufferItem(1)] public virtual IList<InterpretedProto> Deps { get; set; } = new List<InterpretedProto>();
        [FlatBufferItem(2)] public virtual InterpretedProto Prev { get; set; }
    }
}
