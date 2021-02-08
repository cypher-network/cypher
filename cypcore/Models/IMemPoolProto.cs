// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using CYPCore.Consensus.Blockmania;

namespace CYPCore.Models
{
    public interface IMemPoolProto
    {
        InterpretedProto Block { get; set; }
        List<DepProto> Deps { get; set; }
        InterpretedProto Prev { get; set; }

        bool Equals(object obj);
        bool Equals(MemPoolProto other);
        int GetHashCode();
        BlockGraph ToMemPool();
    }
}