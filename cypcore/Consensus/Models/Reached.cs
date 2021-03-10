// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using FlatSharp.Attributes;

namespace CYPCore.Consensus.Models
{
    [FlatBufferTable]
    public class Reached : object
    {
        [FlatBufferItem(0)] public virtual string Hash { get; set; }
        [FlatBufferItem(1)] public virtual ulong Node { get; set; }
        [FlatBufferItem(2)] public virtual ulong Round { get; set; }

        public Reached(string hash, ulong node, ulong round)
        {
            Hash = hash;
            Node = node;
            Round = round;
        }
    }
}