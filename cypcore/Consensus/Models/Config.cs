// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Consensus.Models
{
    public class Config
    {
        public ulong LastInterpreted;
        public ulong[] Nodes;
        public ulong SelfID;
        public ulong TotalNodes;

        public Config(ulong[] nodes, ulong id)
        {
            Nodes = nodes;
            SelfID = id;
            TotalNodes = (ulong)nodes.Length;
        }

        public Config(ulong lastInterpreted, ulong[] nodes, ulong id, ulong totalNodes)
        {
            LastInterpreted = lastInterpreted;
            Nodes = nodes;
            SelfID = id;
            TotalNodes = totalNodes;
        }
    }
}