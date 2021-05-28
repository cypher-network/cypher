// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using MessagePack;

namespace CYPCore.Consensus.Models
{
    [MessagePackObject]
    public class Reached : object
    {
        [Key(0)] public string Hash { get; set; }
        [Key(1)] public ulong Node { get; set; }
        [Key(2)] public ulong Round { get; set; }

        public Reached(string hash, ulong node, ulong round)
        {
            Hash = hash;
            Node = node;
            Round = round;
        }
    }
}