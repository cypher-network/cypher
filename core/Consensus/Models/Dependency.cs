// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;

namespace CypherNetwork.Consensus.Models;

[MessagePack.MessagePackObject]
public record Dependency
{
    [MessagePack.Key(0)]
    public Block Block { get; }
    [MessagePack.Key(1)]
    public IList<Block> Dependencies { get; }
    [MessagePack.Key(2)]
    public Block Prev { get; }

    /// <summary>
    /// 
    /// </summary>
    public Dependency()
    {
        Block = new Block();
        Dependencies = new List<Block>();
        Prev = new Block();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="block"></param>
    public Dependency(Block block)
    {
        Block = block;
        Dependencies = new List<Block>();
        Prev = new Block();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="block"></param>
    /// <param name="dependencies"></param>
    public Dependency(Block block, List<Block> dependencies)
    {
        Block = block;
        Dependencies = dependencies;
        Prev = new Block();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="block"></param>
    /// <param name="dependencies"></param>
    /// <param name="prev"></param>
    public Dependency(Block block, List<Block> dependencies, Block prev)
    {
        Block = block;
        Dependencies = dependencies;
        Prev = prev;
    }
}