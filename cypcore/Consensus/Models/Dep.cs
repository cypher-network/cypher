// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;

namespace CYPCore.Consensus.Models
{
    public class Dep
    {
        public BlockId Block { get; }
        public List<BlockId> Deps { get; }
        public BlockId Prev { get; }

        public Dep()
        {
            Block = new BlockId();
            Deps = new List<BlockId>();
            Prev = new BlockId();
        }

        public Dep(BlockId block)
        {
            Block = block;
            Deps = new List<BlockId>();
            Prev = new BlockId();
        }

        public Dep(BlockId block, List<BlockId> deps)
        {
            Block = block;
            Deps = deps;
            Prev = new BlockId();
        }

        public Dep(BlockId block, List<BlockId> deps, BlockId prev)
        {
            Block = block;
            Deps = deps;
            Prev = prev;
        }
    }
}