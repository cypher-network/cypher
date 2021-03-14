// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class BpProto : object
    {
        [FlatBufferItem(0)] public virtual byte[] Proof { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            if (Proof == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "BpProto.Proof" }));
            }
            if (Proof != null && Proof.Length != 675)
            {
                results.Add(new ValidationResult("Range exception", new[] { "BpProto.Proof" }));
            }

            return results;
        }
    }
}
