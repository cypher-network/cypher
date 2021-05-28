// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.Linq;
using CYPCore.Extensions;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class BlockHeader
    {
        [Key(0)] public int Bits { get; set; }
        [Key(1)] public long Height { get; set; }
        [Key(2)] public long Locktime { get; set; }
        [Key(3)] public string LocktimeScript { get; set; }
        [Key(4)] public string MerkelRoot { get; set; }
        [Key(5)] public string Nonce { get; set; }
        [Key(6)] public string PrevMerkelRoot { get; set; }
        [Key(7)] public string Proof { get; set; }
        [Key(8)] public string Sec { get; set; }
        [Key(9)] public string Seed { get; set; }
        [Key(10)] public ulong Solution { get; set; }
        [Key(11)] public IList<Transaction> Transactions { get; set; } = new List<Transaction>();
        [Key(12)] public int Version { get; set; }
        [Key(13)] public string VrfSignature { get; set; }
        [Key(14)] public string Signature { get; set; }
        [Key(15)] public string PublicKey { get; set; }

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
        public byte[] ToStream()
        {
            using var ts = new Helper.TangramStream();
            ts
                .Append(Bits)
                .Append(Nonce)
                .Append(PrevMerkelRoot)
                .Append(Proof)
                .Append(Sec)
                .Append(Seed)
                .Append(Solution)
                .Append(Locktime)
                .Append(LocktimeScript)
                .Append(NBitcoin.Crypto.Hashes.DoubleSHA256(
                    Helper.Util.Combine(Transactions.Select(x => x.ToHash()).ToArray())).ToBytes(false))
                .Append(Version)
                .Append(VrfSignature);

            return ts.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToFinalStream()
        {
            using var ts = new Helper.TangramStream();

            ts.Append(ToStream());
            ts.Append(MerkelRoot);

            return ts.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToHash()
        {
            return NBitcoin.Crypto.Hashes.DoubleSHA256(ToStream()).ToBytes(false); ;
        }
    }
}

