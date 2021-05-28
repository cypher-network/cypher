// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using MessagePack;

namespace CYPCore.Consensus.Models
{
    [MessagePackObject]
    public class Entry
    {
        [Key(0)] public virtual Block Block { get; set; }
        [Key(1)] public virtual IList<Block> Deps { get; set; } = new List<Block>();
        [Key(2)] public virtual Block Prev { get; set; }

        public Entry(Block prev)
        {
            Prev = prev;
        }

        public Entry(Block block, Block prev)
        {
            Block = block;
            Prev = prev;
        }

        public Entry(Block block, Block[] deps, Block prev)
        {
            Block = block;
            Deps = deps;
            Prev = prev;
        }
    }
}