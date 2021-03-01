// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;

namespace CYPCore.Consensus.Models
{
    public class Interpreted
    {
        public List<BlockId> Blocks { get; }
        public ulong Consumed { get; }
        public ulong Round { get; }

        public Interpreted(List<BlockId> blocks, ulong consumed, ulong round)
        {
            Blocks = blocks;
            Consumed = consumed;
            Round = round;
        }
    }
}