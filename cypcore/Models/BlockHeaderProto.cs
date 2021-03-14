// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Linq;
using System.Collections.Generic;
using CYPCore.Extentions;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class BlockHeaderProto : object
    {
        [FlatBufferItem(0)] public virtual int Bits { get; set; }
        [FlatBufferItem(1)] public virtual long Height { get; set; }
        [FlatBufferItem(2)] public virtual long Locktime { get; set; }
        [FlatBufferItem(3)] public virtual string LocktimeScript { get; set; }
        [FlatBufferItem(4)] public virtual string MerkelRoot { get; set; }
        [FlatBufferItem(5)] public virtual string Nonce { get; set; }
        [FlatBufferItem(6)] public virtual string PrevMerkelRoot { get; set; }
        [FlatBufferItem(7)] public virtual string Proof { get; set; }
        [FlatBufferItem(8)] public virtual string Sec { get; set; }
        [FlatBufferItem(9)] public virtual string Seed { get; set; }
        [FlatBufferItem(10)] public virtual ulong Solution { get; set; }
        [FlatBufferItem(11)] public virtual TransactionProto[] Transactions { get; set; }
        [FlatBufferItem(12)] public virtual int Version { get; set; }
        [FlatBufferItem(13)] public virtual string VrfSig { get; set; }

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
            //TODO: remove .Append(MerkelRoot ?? string.Empty) when we create a new block zero
            using var ts = new Helper.TangramStream();
            ts
                .Append(Bits)
                .Append(MerkelRoot ?? string.Empty)
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
                .Append(VrfSig);

            return NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray()).ToBytes(false); ;
        }
    }
}

