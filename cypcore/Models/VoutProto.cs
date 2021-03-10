// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class VoutProto : object
    {
        [FlatBufferItem(0)] public virtual ulong A { get; set; }
        [FlatBufferItem(1)] public virtual byte[] C { get; set; }
        [FlatBufferItem(2)] public virtual byte[] E { get; set; }
        [FlatBufferItem(3)] public virtual long L { get; set; }
        [FlatBufferItem(4)] public virtual byte[] N { get; set; }
        [FlatBufferItem(5)] public virtual byte[] P { get; set; }
        [FlatBufferItem(6)] public virtual string S { get; set; }

        [FlatBufferItem(7, DefaultValue = CoinType.Coin)]
        public virtual CoinType T { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            if (C == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "VoutProto.C" }));
            }

            if (C != null && C.Length != 33)
            {
                results.Add(new ValidationResult("Range exception", new[] { "VoutProto.C" }));
            }

            if (E == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "VoutProto.E" }));
            }

            if (E != null && E.Length != 33)
            {
                results.Add(new ValidationResult("Range exception", new[] { "VoutProto.E" }));
            }

            if (N == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "VoutProto.N" }));
            }

            if (N != null && N.Length > 241)
            {
                results.Add(new ValidationResult("Range exception", new[] { "VoutProto.N" }));
            }

            if (P == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "VoutProto.P" }));
            }

            if (P != null && P.Length != 33)
            {
                results.Add(new ValidationResult("Range exception", new[] { "VoutProto.P" }));
            }

            if (!string.IsNullOrEmpty(S))
            {
                if (S.Length != 16)
                {
                    results.Add(new ValidationResult("Range exception", new[] { "VoutProto.S" }));
                }
            }

            if (T != CoinType.Coin && T != CoinType.Coinbase && T != CoinType.Coinstake &&
                T != CoinType.fee)
            {
                results.Add(new ValidationResult("Argument exception", new[] { "VoutProto.T" }));
            }

            return results;
        }
    }
}
