// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Extentions;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class SeenBlockHeaderProto : object
    {
        [FlatBufferItem(0)] public virtual string MerkleRoot { get; set; }
        [FlatBufferItem(1)] public virtual string PrevMerkelRoot { get; set; }
        [FlatBufferItem(2)] public virtual bool Published { get; set; }

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
                .Append(MerkleRoot)
                .Append(PrevMerkelRoot);

            return NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray()).ToBytes(false); ;
        }
    }
}
