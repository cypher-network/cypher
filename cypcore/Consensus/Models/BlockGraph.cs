// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using Blake3;
using CYPCore.Extensions;
using MessagePack;
using Newtonsoft.Json;

namespace CYPCore.Consensus.Models
{
    [MessagePackObject]
    public class BlockGraph : IEquatable<BlockGraph>
    {
        [Key(0)] public Block Block { get; set; }
        [Key(1)] public IList<Dep> Deps { get; set; } = new List<Dep>();
        [Key(2)] public Block Prev { get; set; }
        [Key(3)] public byte[] PublicKey { get; set; }
        [Key(4)] public byte[] Signature { get; set; }

        public static bool operator ==(BlockGraph left, BlockGraph right) => Equals(left, right);

        public static bool operator !=(BlockGraph left, BlockGraph right) => !Equals(left, right);

        public override bool Equals(object obj) => obj is BlockGraph blockGraph && Equals(blockGraph);

        public bool Equals(BlockGraph other)
        {
            return ToHash().Xor(other?.ToHash());
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Block, Prev);
        }

        public T Cast<T>()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<T>(json);
        }

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
            if (Block != null)
            {
                ts
                    .Append(Block.Data)
                    .Append(Block.Hash)
                    .Append(Block.Node)
                    .Append(Block.Round);
            }

            if (Prev != null)
            {
                ts
                    .Append(Prev.Data)
                    .Append(Prev.Hash)
                    .Append(Prev.Node)
                    .Append(Prev.Round);
            }

            return ts.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] Serialize()
        {
            return MessagePackSerializer.Serialize(this);
        }
    }
}