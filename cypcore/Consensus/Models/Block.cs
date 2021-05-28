// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Text;
using MessagePack;

namespace CYPCore.Consensus.Models
{
    [MessagePackObject]
    public class Block : IEquatable<Block>
    {
        private const string HexUpper = "0123456789ABCDEF";

        [Key(0)] public string Hash { get; set; }
        [Key(1)] public ulong Node { get; set; }
        [Key(2)] public ulong Round { get; set; }
        [Key(3)] public byte[] Data { get; set; }

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