// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using CypherNetwork.Extensions;
using MessagePack;

namespace CypherNetwork.Models;

[MessagePackObject, Serializable]
public struct Peer : IComparable<Peer>
{
    [Key(0)] public byte[] HttpEndPoint { get; init; }
    [Key(1)] public ulong BlockCount { get; set; }
    [Key(2)] public ulong ClientId { get; init; }
    [Key(3)] public byte[] Listening { get; set; }
    [Key(4)] public byte[] Advertise { get; set; }
    [Key(5)] public byte[] Name { get; set; }
    [Key(6)] public byte[] PublicKey { get; set; }
    [Key(7)] public byte[] Version { get; set; }
    [Key(8)] public long Timestamp { get; set; }

    /// <summary>
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(Peer other)
    {
        if (Equals(this, other)) return 0;
        if (Equals(null, other)) return 1;
        return Advertise.Xor(other.Advertise) ? 0 : 1;
    }
    

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Advertise, Listening, Name, Version, PublicKey);
    }
}