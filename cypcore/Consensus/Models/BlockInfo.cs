// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Consensus.Models
{
    public class BlockInfo
    {
        public ulong Max;
        public BlockGraph Data;

        public BlockInfo(BlockGraph data, ulong max)
        {
            Data = data;
            Max = max;
        }
    }
}