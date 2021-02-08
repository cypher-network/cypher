// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;

using CYPCore.Extentions;

namespace CYPCore.Models
{
    [ProtoContract]
    public class SeenBlockHeaderProto
    {
        [ProtoMember(1)]
        public string MrklRoot { get; set; }
        [ProtoMember(2)]
        public string PrevBlock { get; set; }
        [ProtoMember(3)]
        public bool Published { get; set; }

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
            byte[] hash;
            using (var ts = new Helper.TangramStream())
            {
                ts
                .Append(MrklRoot)
                .Append(PrevBlock);

                hash = NBitcoin.Crypto.Hashes.DoubleSHA256(ts.ToArray()).ToBytes(false);
            }

            return hash;
        }
    }
}
