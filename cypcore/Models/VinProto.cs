// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class VinProto : object
    {
        [FlatBufferItem(0)] public virtual KeyImageProto Key { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            if (Key.Image == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "VinProto.Key.Image" }));
            }

            if (Key.Image != null && Key.Image.Length != 33)
            {
                results.Add(new ValidationResult("Range exception", new[] { "VinProto.Key.Image" }));
            }

            if (Key.Offsets == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "VinProto.Key.Offsets" }));
            }

            if (Key.Offsets != null && Key.Offsets.Length != 1452)
            {
                results.Add(new ValidationResult("Range exception", new[] { "VinProto.Key.Offsets" }));
            }

            return results;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToHash()
        {
            return NBitcoin.Crypto.Hashes.DoubleSHA256(Stream()).ToBytes(false);
        }

        public byte[] Stream()
        {
            using var ts = new Helper.TangramStream();
            ts
                .Append(Key.Image)
                .Append(Key.Offsets);

            return ts.ToArray(); ;
        }
    }
}
