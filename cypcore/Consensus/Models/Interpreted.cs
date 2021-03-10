// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using FlatSharp.Attributes;

namespace CYPCore.Consensus.Models
{
    [FlatBufferTable]
    public class Interpreted
    {
        [FlatBufferItem(0)] public virtual IList<Block> Blocks { get; set; } = new List<Block>();
        [FlatBufferItem(1)] public virtual ulong Consumed { get; set; }
        [FlatBufferItem(2)] public virtual ulong Round { get; set; }

        public Interpreted(IList<Block> blocks, ulong consumed, ulong round)
        {
            Blocks = blocks;
            Consumed = consumed;
            Round = round;
        }
    }
}