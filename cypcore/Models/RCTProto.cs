// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class RCTProto : object
    {
        [FlatBufferItem(0)] public virtual byte[] M { get; set; }
        [FlatBufferItem(1)] public virtual byte[] P { get; set; }
        [FlatBufferItem(2)] public virtual byte[] S { get; set; }
        [FlatBufferItem(3)] public virtual byte[] I { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            if (I == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Rct.I" }));
            }

            if (I != null && I.Length != 32)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Rct.I" }));
            }

            if (M == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Rct.M" }));
            }

            if (M != null && M.Length != 1452)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Rct.M" }));
            }

            if (P == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Rct.P" }));
            }

            if (P != null && P.Length != 32)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Rct.P" }));
            }

            if (S == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Rct.S" }));
            }

            if (S != null && S.Length != 1408)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Rct.S" }));
            }

            return results;
        }
    }
}
