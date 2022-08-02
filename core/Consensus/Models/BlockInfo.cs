// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Consensus.Models;

public class BlockInfo
{
    public BlockInfo(BlockGraph data, ulong max)
    {
        Data = data;
        Max = max;
    }

    public ulong Max { get; set; }
    public BlockGraph Data { get; set; }
}