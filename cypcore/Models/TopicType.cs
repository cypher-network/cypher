// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferEnum(typeof(sbyte))]
    public enum TopicType : sbyte
    {
        AddBlock,
        AddBlocks,
        AddMemoryPool,
        AddMemoryPools,
        AddTransaction,
        AddBlockGraph
    }
}
