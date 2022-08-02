// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;

namespace CypherNetwork.Consensus.Models;

/// <summary>
/// 
/// </summary>
public record Interpreted
{
    public IList<Block> Blocks { get; } = new List<Block>();
    public ulong Consumed { get; }
    public ulong Round { get; }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="blocks"></param>
    /// <param name="consumed"></param>
    /// <param name="round"></param>
    public Interpreted(IList<Block> blocks, ulong consumed, ulong round)
    {
        Blocks = blocks;
        Consumed = consumed;
        Round = round;
    }
}