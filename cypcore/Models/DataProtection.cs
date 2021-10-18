// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Blake3;
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
            return Hasher.Hash(ToStream()).HexToByte();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToStream()
        {
            using Helper.BufferStream ts = new();
            ts.Append(FriendlyName)
                .Append(Payload);
            return ts.ToArray();
        }
    }
}