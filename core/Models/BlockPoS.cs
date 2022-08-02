// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Blake3;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;

namespace CypherNetwork.Models;

[MessagePack.MessagePackObject]
public record BlockPoS
{
    [MessagePack.Key(0)] public uint Bits { get; set; }
    [MessagePack.Key(1)] public ulong Solution { get; set; }
    [MessagePack.Key(2)] public byte[] Nonce { get; set; }
    [MessagePack.Key(3)] public byte[] VrfProof { get; set; }
    [MessagePack.Key(4)] public byte[] VrfSig { get; set; }
    [MessagePack.Key(5)] public byte[] PublicKey { get; set; }

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
    public byte[] ToIdentifier()
    {
        return ToHash().ByteToHex().ToBytes();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] ToStream()
    {
        if (Validate().Any()) return null;
        using var ts = new BufferStream();
        ts.Append(Bits).Append(Solution).Append(Nonce).Append(VrfProof).Append(VrfSig).Append(PublicKey);
        return ts.ToArray();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ValidationResult> Validate()
    {
        var results = new List<ValidationResult>();
        if (Bits <= 0) results.Add(new ValidationResult("Range exception", new[] { "Bits" }));
        if (Solution <= 0) results.Add(new ValidationResult("Range exception", new[] { "Solution" }));
        if (Nonce == null) results.Add(new ValidationResult("Argument is null", new[] { "Nonce" }));
        if (Nonce is { Length: > 77 }) results.Add(new ValidationResult("Range exception", new[] { "Nonce" }));
        if (VrfProof == null) results.Add(new ValidationResult("Argument is null", new[] { "VrfProof" }));
        if (VrfProof != null && VrfProof.Length != 96)
            results.Add(new ValidationResult("Range exception", new[] { "VrfProof" }));
        if (VrfSig == null) results.Add(new ValidationResult("Argument is null", new[] { "VrfSig" }));
        if (VrfSig != null && VrfSig.Length != 32)
            results.Add(new ValidationResult("Range exception", new[] { "VrfSig" }));
        if (PublicKey == null) results.Add(new ValidationResult("Argument is null", new[] { "PublicKey" }));
        if (PublicKey != null && PublicKey.Length != 33)
            results.Add(new ValidationResult("Range exception", new[] { "PublicKey" }));
        return results;
    }
}