// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using FlatSharp.Attributes;

namespace CYPCore.Consensus.Models
{
    [FlatBufferTable]
    public class BlockInfo : object
    {
        [FlatBufferItem(0)] public virtual ulong Max { get; set; }
        [FlatBufferItem(1)] public virtual BlockGraph Data { get; set; }

        public BlockInfo(BlockGraph data, ulong max)
        {
            Data = data;
            Max = max;
        }
    }
}