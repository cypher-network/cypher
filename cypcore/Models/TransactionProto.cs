// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using CYPCore.Extentions;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    public interface ITransactionProto
    {
        byte[] TxnId { get; set; }
        BpProto[] Bp { get; set; }
        int Ver { get; set; }
        int Mix { get; set; }
        VinProto[] Vin { get; set; }
        VoutProto[] Vout { get; set; }
        RCTProto[] Rct { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IEnumerable<ValidationResult> Validate();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        byte[] ToIdentifier();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        byte[] ToHash();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        byte[] Stream();

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        T Cast<T>();
    }

    [FlatBufferTable]
    public class TransactionProto : object, ITransactionProto
    {
        [FlatBufferItem(0)] public virtual byte[] TxnId { get; set; }
        [FlatBufferItem(1)] public virtual BpProto[] Bp { get; set; }
        [FlatBufferItem(2)] public virtual int Ver { get; set; }
        [FlatBufferItem(3)] public virtual int Mix { get; set; }
        [FlatBufferItem(4)] public virtual VinProto[] Vin { get; set; }
        [FlatBufferItem(5)] public virtual VoutProto[] Vout { get; set; }
        [FlatBufferItem(6)] public virtual RCTProto[] Rct { get; set; }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            if (TxnId == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "TxnId" }));
            }
            if (TxnId != null && TxnId.Length != 32)
            {
                results.Add(new ValidationResult("Range exception", new[] { "TxnId" }));
            }
            if (Mix < 0)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Mix" }));
            }
            if (Mix != 22)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Mix" }));
            }
            if (Rct == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Rct" }));
            }
            if (Ver != 0x1)
            {
                results.Add(new ValidationResult("Incorrect number", new[] { "Ver" }));
            }
            if (Vin == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vin" }));
            }
            if (Vout == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vout" }));
            }

            foreach (var bp in Bp)
            {
                results.AddRange(bp.Validate());
            }

            if (Vin != null)
                foreach (var vi in Vin)
                {
                    results.AddRange(vi.Validate());
                }

            if (Vout != null)
                foreach (var vo in Vout)
                {
                    results.AddRange(vo.Validate());
                }

            if (Rct == null) return results;

            foreach (var rct in Rct)
            {
                results.AddRange(rct.Validate());
            }

            return results;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToIdentifier()
        {
            return ToHash().ByteToHex().ToBytes();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToHash()
        {
            return NBitcoin.Crypto.Hashes.DoubleSHA256(Stream()).ToBytes(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] Stream()
        {
            using var ts = new Helper.TangramStream();
            ts
                .Append(TxnId)
                .Append(Mix)
                .Append(Ver);

            foreach (var bp in Bp)
            {
                ts.Append(bp.Proof);
            }

            foreach (var vin in Vin)
            {
                ts.Append(vin.Key.Image);
                ts.Append(vin.Key.Offsets);
            }

            foreach (var vout in Vout)
            {
                ts
                    .Append(vout.A)
                    .Append(vout.C)
                    .Append(vout.E)
                    .Append(vout.L)
                    .Append(vout.N)
                    .Append(vout.P)
                    .Append(vout.S ?? string.Empty)
                    .Append(vout.T.ToString());
            }

            foreach (var rct in Rct)
            {
                ts
                    .Append(rct.I)
                    .Append(rct.M)
                    .Append(rct.P)
                    .Append(rct.S);
            }

            return ts.ToArray(); ;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Cast<T>()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
