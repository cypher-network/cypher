// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Blake3;
using Newtonsoft.Json;
using CYPCore.Extensions;
using MessagePack;

namespace CYPCore.Models
{
    public interface ITransaction
    {
        byte[] TxnId { get; set; }
        Bp[] Bp { get; set; }
        int Ver { get; set; }
        int Mix { get; set; }
        Vin[] Vin { get; set; }
        Vout[] Vout { get; set; }
        Rct[] Rct { get; set; }

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
        byte[] ToStream();

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        T Cast<T>();
    }

    [MessagePackObject]
    public class Transaction : IEquatable<Transaction>, ITransaction
    {
        [MessagePack.Key(0)] public byte[] TxnId { get; set; }
        [MessagePack.Key(1)] public Bp[] Bp { get; set; }
        [MessagePack.Key(2)] public int Ver { get; set; }
        [MessagePack.Key(3)] public int Mix { get; set; }
        [MessagePack.Key(4)] public Vin[] Vin { get; set; }
        [MessagePack.Key(5)] public Vout[] Vout { get; set; }
        [MessagePack.Key(6)] public Rct[] Rct { get; set; }
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
            if (Ver != 0x2)
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
            if (Bp != null)
            {
                foreach (var bp in Bp)
                {
                    results.AddRange(bp.Validate());
                }
            }
            if (Vin != null)
            {
                foreach (var vi in Vin)
                {
                    results.AddRange(vi.Validate());
                }
            }
            if (Vout != null)
            {
                foreach (var vo in Vout)
                {
                    results.AddRange(vo.Validate());
                }
            }
            if (Rct != null)
            {
                foreach (var rct in Rct)
                {
                    results.AddRange(rct.Validate());
                }
            }
            if (Vtime == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Vtime" }));
            }
            if (Vtime != null)
            {
                results.AddRange(Vtime.Validate());
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
            return Hasher.Hash(ToStream()).HexToByte();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToStream()
        {
            using var ts = new Helper.TangramStream();
            ts
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
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(Transaction left, Transaction right) => Equals(left, right);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(Transaction left, Transaction right) => !Equals(left, right);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj) => obj is Transaction transactionModel && Equals(transactionModel);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(Transaction other)
        {
            return TxnId.Xor(other?.TxnId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(TxnId);
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
