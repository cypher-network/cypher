// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Text;
using Blake3;
using Newtonsoft.Json;
using CYPCore.Extensions;
using MessagePack;

namespace CYPCore.Models
{
    public interface IInterpreted
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
        byte[] ToStream();
        T Cast<T>();
    }

    [MessagePackObject]
    public class Interpreted : IInterpreted
    {
        private const string HexUpper = "0123456789ABCDEF";

        [Key(0)] public string Hash { get; set; }
        [Key(1)] public ulong Node { get; set; }
        [Key(2)] public ulong Round { get; set; }
        [Key(3)] public object Data { get; set; }
        [Key(4)] public string PublicKey { get; set; }
        [Key(5)] public string Signature { get; set; }
        [Key(6)] public string PreviousHash { get; set; }

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
            return Hasher.Hash(ToStream()).HexToByte();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToStream()
        {
            using var ts = new Helper.BufferStream();
            ts.Append(Hash)
                .Append(Node)
                .Append(PreviousHash ?? string.Empty)
                .Append(Round)
                .Append(PublicKey ?? string.Empty)
                .Append(Signature ?? string.Empty);
            return ts.ToArray();
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
