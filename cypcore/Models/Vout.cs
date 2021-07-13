// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class Vout
    {
        [MessagePack.Key(0)] public ulong A { get; set; }
        [MessagePack.Key(1)] public byte[] C { get; set; }
        [MessagePack.Key(2)] public byte[] E { get; set; }
        [MessagePack.Key(3)] public long L { get; set; }
        [MessagePack.Key(4)] public byte[] N { get; set; }
        [MessagePack.Key(5)] public byte[] P { get; set; }
        [MessagePack.Key(6)] public string S { get; set; }
        [MessagePack.Key(7)] public CoinType T { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();
            if (C == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vout.C" }));
            }
            if (C != null && C.Length != 33)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Vout.C" }));
            }
            if (E == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vout.E" }));
            }
            if (E != null && E.Length != 33)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Vout.E" }));
            }
            if (N == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vout.N" }));
            }
            if (N is { Length: > 512 })
            {
                results.Add(new ValidationResult("Range exception", new[] { "Vout.N" }));
            }
            if (P == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vout.P" }));
            }
            if (P != null && P.Length != 33)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Vout.P" }));
            }
            if (!string.IsNullOrEmpty(S))
            {
                if (S.Length != 16)
                {
                    results.Add(new ValidationResult("Range exception", new[] { "Vout.S" }));
                }
            }
            if (T != CoinType.Payment && T != CoinType.Coinbase && T != CoinType.Coinstake && T != CoinType.Change)
            {
                results.Add(new ValidationResult("Argument exception", new[] { "Vout.T" }));
            }
            return results;
        }
    }
}
