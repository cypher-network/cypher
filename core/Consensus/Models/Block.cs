// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Text;

namespace CypherNetwork.Consensus.Models;

[MessagePack.MessagePackObject]
public class Block : IEquatable<Block>
{
    private const string HexUpper = "0123456789ABCDEF";

    [MessagePack.Key(0)]
    public string Hash { get; set; }
    [MessagePack.Key(1)]
    public ulong Node { get; set; }
    [MessagePack.Key(2)]
    public ulong Round { get; set; }
    [MessagePack.Key(3)]
    public byte[] Data { get; set; }
    [MessagePack.Key(4)]
    public string DataHash { get; set; }
    [MessagePack.Key(5)]
    public byte[] BlockHash { get; set; }
    
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

    public Block(string hash, ulong node, ulong round, byte[] data, string dataHash, byte[] blockHash)
    {
        Hash = hash;
        Node = node;
        Round = round;
        Data = data;
        DataHash = dataHash;
        BlockHash = blockHash;
    }

    public bool Equals(Block blockId)
    {
        return blockId != null
               && blockId.Node == Node
               && blockId.Round == Round
               && Hash.Equals(blockId.Hash);
    }

    public bool Valid()
    {
        return Hash != string.Empty;
    }

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
            v.Append(new[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
        }

        return v.ToString();
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