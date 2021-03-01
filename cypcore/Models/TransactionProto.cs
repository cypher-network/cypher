// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using Newtonsoft.Json;

using ProtoBuf;

using CYPCore.Extentions;

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

    [ProtoContract]
    public class TransactionProto : ITransactionProto
    {
        public static TransactionProto CreateInstance()
        {
            return new TransactionProto();
        }

        [ProtoMember(1)] public byte[] TxnId { get; set; }
        [ProtoMember(2)] public BpProto[] Bp { get; set; }
        [ProtoMember(3)] public int Ver { get; set; }
        [ProtoMember(4)] public int Mix { get; set; }
        [ProtoMember(5)] public VinProto[] Vin { get; set; }
        [ProtoMember(6)] public VoutProto[] Vout { get; set; }
        [ProtoMember(7)] public RCTProto[] Rct { get; set; }

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
                results.Add(new ValidationResult("Range exeption", new[] { "TxnId" }));
            }
            if (Mix < 0)
            {
                results.Add(new ValidationResult("Range exeption", new[] { "Mix" }));
            }
            if (Mix != 22)
            {
                results.Add(new ValidationResult("Range exeption", new[] { "Mix" }));
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
                if (bp.Proof == null)
                {
                    results.Add(new ValidationResult("Argument is null", new[] { "Bp.Proof" }));
                }
                if (bp.Proof != null && bp.Proof.Length != 675)
                {
                    results.Add(new ValidationResult("Range exeption", new[] { "Bp.Proof" }));
                }
            }

            if (Vin != null)
                foreach (var vi in Vin)
                {
                    if (vi.Key.K_Image == null)
                    {
                        results.Add(new ValidationResult("Argument is null", new[] { "Vin.Key.K_Image" }));
                    }

                    if (vi.Key.K_Image != null && vi.Key.K_Image.Length != 33)
                    {
                        results.Add(new ValidationResult("Range exeption", new[] { "Vin.Key.K_Image" }));
                    }

                    if (vi.Key.K_Offsets == null)
                    {
                        results.Add(new ValidationResult("Argument is null", new[] { "Vin.Key.K_Offsets" }));
                    }

                    if (vi.Key.K_Offsets != null && vi.Key.K_Offsets.Length != 1452)
                    {
                        results.Add(new ValidationResult("Range exeption", new[] { "Vin.Key.K_Offsets" }));
                    }
                }

            if (Vout != null)
                foreach (var vo in Vout)
                {
                    if (vo.C == null)
                    {
                        results.Add(new ValidationResult("Argument is null", new[] { "Vout.C" }));
                    }

                    if (vo.C.Length != 33)
                    {
                        results.Add(new ValidationResult("Range exeption", new[] { "Vout.C" }));
                    }

                    if (vo.E == null)
                    {
                        results.Add(new ValidationResult("Argument is null", new[] { "Vout.E" }));
                    }

                    if (vo.E.Length != 33)
                    {
                        results.Add(new ValidationResult("Range exeption", new[] { "Vout.E" }));
                    }

                    if (vo.N == null)
                    {
                        results.Add(new ValidationResult("Argument is null", new[] { "Vout.N" }));
                    }

                    if (vo.N.Length > 241)
                    {
                        results.Add(new ValidationResult("Range exeption", new[] { "Vout.N" }));
                    }

                    if (vo.P == null)
                    {
                        results.Add(new ValidationResult("Argument is null", new[] { "Vout.P" }));
                    }

                    if (vo.P != null && vo.P.Length != 33)
                    {
                        results.Add(new ValidationResult("Range exeption", new[] { "Vout.P" }));
                    }

                    if (!string.IsNullOrEmpty(vo.S))
                    {
                        if (vo.S.Length != 16)
                        {
                            results.Add(new ValidationResult("Range exeption", new[] { "Vout.S" }));
                        }
                    }

                    if (vo.T != CoinType.Coin && vo.T != CoinType.Coinbase && vo.T != CoinType.Coinstake &&
                        vo.T != CoinType.fee)
                    {
                        results.Add(new ValidationResult("Argument exeption", new[] { "Vout.T" }));
                    }
                }

            if (Rct == null) return results;

            foreach (var rct in Rct)
            {
                if (rct.I == null)
                {
                    results.Add(new ValidationResult("Argument is null", new[] { "Rct.I" }));
                }

                if (rct.I != null && rct.I.Length != 32)
                {
                    results.Add(new ValidationResult("Range exeption", new[] { "Rct.I" }));
                }

                if (rct.M == null)
                {
                    results.Add(new ValidationResult("Argument is null", new[] { "Rct.M" }));
                }

                if (rct.M != null && rct.M.Length != 1452)
                {
                    results.Add(new ValidationResult("Range exeption", new[] { "Rct.M" }));
                }

                if (rct.P == null)
                {
                    results.Add(new ValidationResult("Argument is null", new[] { "Rct.P" }));
                }

                if (rct.P != null && rct.P.Length != 32)
                {
                    results.Add(new ValidationResult("Range exeption", new[] { "Rct.P" }));
                }

                if (rct.S == null)
                {
                    results.Add(new ValidationResult("Argument is null", new[] { "Rct.S" }));
                }

                if (rct.S != null && rct.S.Length != 1408)
                {
                    results.Add(new ValidationResult("Range exeption", new[] { "Rct.S" }));
                }
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
                ts.Append(vin.Key.K_Image);
                ts.Append(vin.Key.K_Offsets);
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
