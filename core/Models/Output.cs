// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MessagePack;

namespace CypherNetwork.Models;

[MessagePackObject]
public record Output
{
    [MessagePack.Key(0)] public byte[] C { get; init; }
    [MessagePack.Key(1)] public byte[] E { get; init; }
    [MessagePack.Key(2)] public byte[] N { get; init; }
    [MessagePack.Key(3)] public CoinType T { get; init; }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ValidationResult> HasErrors()
    {
        var results = new List<ValidationResult>();
        if (C == null) results.Add(new ValidationResult("Argument is null", new[] { "Output.C" }));
        if (C != null && C.Length != 33) results.Add(new ValidationResult("Range exception", new[] { "Output.C" }));
        if (E == null) results.Add(new ValidationResult("Argument is null", new[] { "Output.E" }));
        if (E != null && E.Length != 33) results.Add(new ValidationResult("Range exception", new[] { "Output.E" }));
        if (N == null) results.Add(new ValidationResult("Argument is null", new[] { "Output.N" }));
        if (N is { Length: > 512 }) results.Add(new ValidationResult("Range exception", new[] { "Output.N" }));
        return results;
    }
}