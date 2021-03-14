// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using FlatSharp.Attributes;

namespace CYPCore.Consensus.Models
{
    [FlatBufferTable]
    public class Dep : object
    {
        [FlatBufferItem(0)] public virtual Block Block { get; }
        [FlatBufferItem(1)] public virtual IList<Block> Deps { get; } = new List<Block>();
        [FlatBufferItem(2)] public virtual Block Prev { get; }

        public Dep()
        {
            Block = new Block();
            Deps = new List<Block>();
            Prev = new Block();
        }

        public Dep(Block block)
        {
            Block = block;
            Deps = new List<Block>();
            Prev = new Block();
        }

        public Dep(Block block, List<Block> deps)
        {
            Block = block;
            Deps = deps;
            Prev = new Block();
        }

        public Dep(Block block, List<Block> deps, Block prev)
        {
            Block = block;
            Deps = deps;
            Prev = prev;
        }
    }
}