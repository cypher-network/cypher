// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;

namespace CypherNetwork.Consensus.Models;

public record Entry
{
    public Block Block { get; }
    public IList<Block> Dependencies { get; set; } = new List<Block>();

    public Block Prev { get; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="prev"></param>
    public Entry(Block prev)
    {
        Prev = prev;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="block"></param>
    /// <param name="prev"></param>
    public Entry(Block block, Block prev)
    {
        Block = block;
        Prev = prev;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="block"></param>
    /// <param name="dependencies"></param>
    /// <param name="prev"></param>
    public Entry(Block block, Block[] dependencies, Block prev)
    {
        Block = block;
        Dependencies = dependencies;
        Prev = prev;
    }
}