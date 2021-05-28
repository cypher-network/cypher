// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class Bp
    {
        [MessagePack.Key(0)] public byte[] Proof { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            if (Proof == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Bp.Proof" }));
            }
            if (Proof != null && Proof.Length != 675)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Bp.Proof" }));
            }

            return results;
        }
    }
}
