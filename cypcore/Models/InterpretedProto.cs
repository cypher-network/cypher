// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Text;
using Newtonsoft.Json;
using CYPCore.Extentions;
using FlatSharp.Attributes;

namespace CYPCore.Models
{
    public interface IInterpretedProto
    {
        string Hash { get; set; }
        ulong Node { get; set; }
        ulong Round { get; set; }
        object Data { get; set; }
        string PublicKey { get; set; }
        string Signature { get; set; }
        string PreviousHash { get; set; }
        string ToString();
        byte[] ToHash();
        byte[] Stream();
        T Cast<T>();
    }

    [FlatBufferTable]
    public class InterpretedProto : object, IInterpretedProto
    {
        private const string HexUpper = "0123456789ABCDEF";

        [FlatBufferItem(0)] public virtual string Hash { get; set; }
        [FlatBufferItem(1)] public virtual ulong Node { get; set; }
        [FlatBufferItem(2)] public virtual ulong Round { get; set; }
        [FlatBufferItem(3)] public virtual object Data { get; set; }
        [FlatBufferItem(4)] public virtual string PublicKey { get; set; }
        [FlatBufferItem(5)] public virtual string Signature { get; set; }
        [FlatBufferItem(6)] public virtual string PreviousHash { get; set; }

        [FlatBufferItem(7, DefaultValue = Models.Status.None)]
        public virtual Status Status { get; set; }

        public override string ToString()
        {
            var v = new StringBuilder();
            v.Append(Node);
            v.Append(" | ");
            v.Append(Round);
            if (string.IsNullOrEmpty(Hash)) return v.ToString();
            v.Append(" | ");
            for (var i = 6; i < 12; i++)
            {
                var c = Hash[i];
                v.Append(new char[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
            }
            return v.ToString();
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
                .Append(Hash)
                .Append(Node)
                .Append(PreviousHash ?? string.Empty)
                .Append(Round)
                .Append(PublicKey ?? string.Empty)
                .Append(Signature ?? string.Empty);

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
