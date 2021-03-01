// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Consensus.Models
{
    public class Entry
    {
        public BlockId Block;
        public BlockId[] Deps;
        public BlockId Prev;

        public Entry(BlockId prev)
        {
            Prev = prev;
        }

        public Entry(BlockId block, BlockId prev)
        {
            Block = block;
            Prev = prev;
        }

        public Entry(BlockId block, BlockId[] deps, BlockId prev)
        {
            Block = block;
            Deps = deps;
            Prev = prev;
        }
    }
}