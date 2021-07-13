// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class Vtime
    {
        [MessagePack.Key(0)] public long W { get; set; }
        [MessagePack.Key(1)] public byte[] M { get; set; }
        [MessagePack.Key(2)] public byte[] N { get; set; }
        [MessagePack.Key(3)] public int I { get; set; }
        [MessagePack.Key(4)] public string S { get; set; }
        [MessagePack.Key(5)] public long L { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();
            if (W <= 0)
            {
                results.Add(new ValidationResult("Range exception", new[] { "W" }));
            }
            if (M == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "M" }));
            }
            if (M != null && M.Length != 32)
            {
                results.Add(new ValidationResult("Range exception", new[] { "M" }));
            }
            if (N == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "N" }));
            }
            if (N is { Length: > 77 })
            {
                results.Add(new ValidationResult("Range exception", new[] { "N" }));
            }
            if (I <= 0)
            {
                results.Add(new ValidationResult("Range exception", new[] { "I" }));
            }
            try
            {
                DateTimeOffset.FromUnixTimeSeconds(L);
            }
            catch (ArgumentOutOfRangeException)
            {
                results.Add(new ValidationResult("Range exception", new[] { "L" }));
            }
            if (S != null && S.Length != 16)
            {
                results.Add(new ValidationResult("Range exception", new[] { "S" }));
            }
            return results;
        }
    }
}