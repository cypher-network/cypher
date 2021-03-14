// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Text;
using FlatSharp.Attributes;

namespace CYPCore.Consensus.Models
{
    [FlatBufferTable]
    public class Block : object, IEquatable<Block>
    {
        private const string HexUpper = "0123456789ABCDEF";

        [FlatBufferItem(0)] public virtual string Hash { get; set; }
        [FlatBufferItem(1)] public virtual ulong Node { get; set; }
        [FlatBufferItem(2)] public virtual ulong Round { get; set; }
        [FlatBufferItem(3)] public virtual byte[] Data { get; set; }

        public Block()
        {
            Hash = string.Empty;
            Node = 0;
            Round = 0;
        }

        public Block(string hash)
        {
            Hash = hash;
            Node = 0;
            Round = 0;
        }

        public Block(string hash, ulong node, ulong round)
        {
            Hash = hash;
            Node = node;
            Round = round;
        }

        public Block(string hash, ulong node, ulong round, byte[] data)
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

            if (string.IsNullOrEmpty(Hash)) return v.ToString();
            v.Append(" | ");
            for (var i = 6; i < 12; i++)
            {
                var c = Hash[i];
                v.Append(new char[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
            }

            return v.ToString();
        }

        public bool Equals(Block blockId)
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
            return Equals(obj as Block);
        }
    }
}