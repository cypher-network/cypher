// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using MessagePack;

namespace CYPCore.Consensus.Models
{
    [MessagePackObject]
    public class Dep : object
    {
        [Key(0)] public virtual Block Block { get; }
        [Key(1)] public virtual IList<Block> Deps { get; } = new List<Block>();
        [Key(2)] public virtual Block Prev { get; }

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