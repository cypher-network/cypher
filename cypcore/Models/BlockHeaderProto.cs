// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using ProtoBuf;

using CYPCore.Extentions;

namespace CYPCore.Models
{
    [ProtoContract]
    public class BlockHeaderProto
    {
        [ProtoMember(1)] public int Bits { get; set; }
        [ProtoMember(2)] public long Height { get; set; }
        [ProtoMember(3)] public long Locktime { get; set; }
        [ProtoMember(4)] public string LocktimeScript { get; set; }
        [ProtoMember(5)] public string MerkelRoot { get; set; }
        [ProtoMember(6)] public string Nonce { get; set; }
        [ProtoMember(7)] public string PrevMerkelRoot { get; set; }
        [ProtoMember(8)] public string Proof { get; set; }
        [ProtoMember(9)] public string Sec { get; set; }
        [ProtoMember(10)] public string Seed { get; set; }
        [ProtoMember(11)] public ulong Solution { get; set; }
        [ProtoMember(12)] public HashSet<TransactionProto> Transactions { get; set; }
        [ProtoMember(13)] public int Version { get; set; }
        [ProtoMember(14)] public string VrfSig { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

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
                .Append(Helper.Util.SHA384ManagedHash(
                    Helper.Util.Combine(Transactions.Select(x => x.ToHash()).ToArray())))
                .Append(Version)
                .Append(VrfSig);

            return NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray()).ToBytes(false); ;
        }
    }
}

