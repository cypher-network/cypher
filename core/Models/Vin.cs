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
public record Vin
{
    [MessagePack.Key(0)] public byte[] Image { get; set; }
    [MessagePack.Key(1)] public byte[] Offsets { get; set; }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ValidationResult> Validate()
    {
        var results = new List<ValidationResult>();
        if (Image == null) results.Add(new ValidationResult("Argument is null", new[] { "Vin.Key.Image" }));
        if (Image != null && Image.Length != 33)
            results.Add(new ValidationResult("Range exception", new[] { "Vin.Key.Image" }));
        if (Offsets == null) results.Add(new ValidationResult("Argument is null", new[] { "Vin.Key.Offsets" }));
        if (Offsets != null && Offsets.Length != 726)
            results.Add(new ValidationResult("Range exception", new[] { "Vin.Key.Offsets" }));
        return results;
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
        if (Validate().Any()) return null;

        using var ts = new BufferStream();
        ts
            .Append(Image)
            .Append(Offsets);
        return ts.ToArray();
    }
}