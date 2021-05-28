// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Extensions;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class DataProtection
    {
        [Key(0)] public string FriendlyName { get; set; }
        [Key(1)] public string Payload { get; set; }

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
            using Helper.TangramStream ts = new();
            ts
                .Append(FriendlyName)
                .Append(Payload);

            return ts.ToArray();
            ;
        }
    }
}