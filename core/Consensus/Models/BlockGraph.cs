// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using Blake3;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using MessagePack;

namespace CypherNetwork.Consensus.Models;

[MessagePackObject]
public record BlockGraph
{
    [Key(0)]
    public Block Block { get; init; }
    [Key(1)]
    public IList<Dependency> Dependencies { get; } = new List<Dependency>();
    [Key(2)]
    public Block Prev { get; init; }
    [Key(3)]
    public byte[] PublicKey { get; set; }
    [Key(4)]
    public byte[] Signature { get; set; }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Block, Prev);
    }

    public byte[] ToIdentifier()
    {
        return ToHash().ByteToHex().ToBytes();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] ToHash()
    {
        return Hasher.Hash(ToStream()).HexToByte();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] ToStream()
    {
        using var ts = new BufferStream();
        if (Block != null)
            ts
                .Append(Block.Data)
                .Append(Block.Hash)
                .Append(Block.Node)
                .Append(Block.Round);

        if (Prev != null)
            ts
                .Append(Prev.Data)
                .Append(Prev.Hash)
                .Append(Prev.Node)
                .Append(Prev.Round);

        return ts.ToArray();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] Serialize()
    {
        return MessagePackSerializer.Serialize(this);
    }
}