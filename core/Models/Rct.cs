// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace CypherNetwork.Models;

[MessagePack.MessagePackObject]
public record Rct
{
    [MessagePack.Key(0)] public byte[] M { get; init; }
    [MessagePack.Key(1)] public byte[] P { get; init; }
    [MessagePack.Key(2)] public byte[] S { get; init; }
    [MessagePack.Key(3)] public byte[] I { get; init; }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ValidationResult> Validate()
    {
        var results = new List<ValidationResult>();
        if (I == null) results.Add(new ValidationResult("Argument is null", new[] { "Rct.I" }));
        if (I != null && I.Length != 32) results.Add(new ValidationResult("Range exception", new[] { "Rct.I" }));
        if (M == null) results.Add(new ValidationResult("Argument is null", new[] { "Rct.M" }));
        if (M != null && M.Length != 1452) results.Add(new ValidationResult("Range exception", new[] { "Rct.M" }));
        if (P == null) results.Add(new ValidationResult("Argument is null", new[] { "Rct.P" }));
        if (P != null && P.Length != 32) results.Add(new ValidationResult("Range exception", new[] { "Rct.P" }));
        if (S == null) results.Add(new ValidationResult("Argument is null", new[] { "Rct.S" }));
        if (S != null && S.Length != 1408) results.Add(new ValidationResult("Range exception", new[] { "Rct.S" }));
        return results;
    }
}