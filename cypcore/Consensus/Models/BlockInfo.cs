// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0


using MessagePack;

namespace CYPCore.Consensus.Models
{
    [MessagePackObject]
    public class BlockInfo
    {
        [Key(0)] public ulong Max { get; set; }
        [Key(1)] public BlockGraph Data { get; set; }

        public BlockInfo(BlockGraph data, ulong max)
        {
            Data = data;
            Max = max;
        }
    }
}