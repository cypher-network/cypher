// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Text;

namespace CYPCore.Consensus.Models
{
    public class BlockId : IEquatable<BlockId>
    {
        private const string HexUpper = "0123456789ABCDEF";

        public string Hash { get; }
        public ulong Node { get; }
        public ulong Round { get; }
        public object Data { get; }

        public BlockId()
        {
            Hash = string.Empty;
            Node = 0;
            Round = 0;
        }

        public BlockId(string hash)
        {
            Hash = hash;
            Node = 0;
            Round = 0;
        }

        public BlockId(string hash, ulong node, ulong round)
        {
            Hash = hash;
            Node = node;
            Round = round;
        }

        public BlockId(string hash, ulong node, ulong round, object data)
        {
            Hash = hash;
            Node = node;
            Round = round;
            Data = data;
        }

        public bool Valid() => Hash != string.Empty;

        public override string ToString()
        {
            var v = new StringBuilder();
            v.Append(Node);
            v.Append(" | ");
            v.Append(Round);

            if (!string.IsNullOrEmpty(Hash))
            {
                v.Append(" | ");
                for (int i = 6; i < 12; i++)
                {
                    var c = Hash[i];
                    v.Append(new char[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
                }
            }

            return v.ToString();
        }

        public bool Equals(BlockId blockId)
        {
            return blockId != null
                   && blockId.Node == Node
                   && blockId.Round == Round
                   && Hash.Equals(blockId.Hash);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Node, Round, Hash);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as BlockId);
        }
    }
}