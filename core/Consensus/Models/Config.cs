// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Consensus.Models;

/// <summary>
/// 
/// </summary>
public record Config
{
    public ulong LastInterpreted { get; }
    public ulong[] Nodes { get; }
    public ulong SelfId { get; }
    public ulong TotalNodes { get; }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="nodes"></param>
    /// <param name="id"></param>
    public Config(ulong[] nodes, ulong id)
    {
        Nodes = nodes;
        SelfId = id;
        TotalNodes = (ulong)nodes.Length;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="lastInterpreted"></param>
    /// <param name="nodes"></param>
    /// <param name="id"></param>
    /// <param name="totalNodes"></param>
    public Config(ulong lastInterpreted, ulong[] nodes, ulong id, ulong totalNodes)
    {
        LastInterpreted = lastInterpreted;
        Nodes = nodes;
        SelfId = id;
        TotalNodes = totalNodes;
    }
}