// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Consensus.Models
{
    public class Reached
    {
        public string Hash { get; }
        public ulong Node { get; }
        public ulong Round { get; }

        public Reached(string hash, ulong node, ulong round)
        {
            Hash = hash;
            Node = node;
            Round = round;
        }
    }
}