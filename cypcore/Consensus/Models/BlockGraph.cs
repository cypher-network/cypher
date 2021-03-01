// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;

namespace CYPCore.Consensus.Models
{
    public class BlockGraph
    {
        public BlockId Block { get; }
        public List<Dep> Deps { get; }
        public BlockId Prev { get; }

        public BlockGraph()
        {
            Block = new BlockId();
            Deps = new List<Dep>();
            Prev = new BlockId();
        }

        public BlockGraph(BlockId block)
        {
            Block = block;
            Deps = new List<Dep>();
            Prev = new BlockId();
        }

        public BlockGraph(BlockId block, BlockId prev)
        {
            Block = block;
            Prev = prev;
            Deps = new List<Dep>();
        }

        public BlockGraph(BlockId block, List<Dep> deps, BlockId prev)
        {
            Block = block;
            Deps = deps;
            Prev = prev;
        }
    }
}