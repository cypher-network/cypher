// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using MessagePack;

namespace CYPCore.Consensus.Models
{
    [MessagePackObject]
    public class Interpreted
    {
        [Key(0)] public IList<Block> Blocks { get; set; } = new List<Block>();
        [Key(1)] public ulong Consumed { get; set; }
        [Key(2)] public ulong Round { get; set; }

        public Interpreted(IList<Block> blocks, ulong consumed, ulong round)
        {
            Blocks = blocks;
            Consumed = consumed;
            Round = round;
        }
    }
}