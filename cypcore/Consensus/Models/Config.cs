// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using FlatSharp.Attributes;

namespace CYPCore.Consensus.Models
{
    [FlatBufferTable]
    public class Config : object
    {
        [FlatBufferItem(0)] public virtual ulong LastInterpreted { get; set; }
        [FlatBufferItem(1)] public virtual ulong[] Nodes { get; set; }
        [FlatBufferItem(2)] public virtual ulong SelfId { get; set; }
        [FlatBufferItem(3)] public virtual ulong TotalNodes { get; set; }

        public Config(ulong[] nodes, ulong id)
        {
            Nodes = nodes;
            SelfId = id;
            TotalNodes = (ulong)nodes.Length;
        }

        public Config(ulong lastInterpreted, ulong[] nodes, ulong id, ulong totalNodes)
        {
            LastInterpreted = lastInterpreted;
            Nodes = nodes;
            SelfId = id;
            TotalNodes = totalNodes;
        }
    }
}