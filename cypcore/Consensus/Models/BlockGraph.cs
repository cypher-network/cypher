// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
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

        public override bool Equals(object obj) => (obj is BlockGraph blockGraph) && Equals(blockGraph);

        public bool Equals(BlockGraph other)
        {
            return (Block.Hash, Block.Node, Block.Round, Deps.Count) == (other.Block.Hash, other.Block.Node,
                other.Block.Round, other.Deps.Count);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Block, Deps, Prev);
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
            return NBitcoin.Crypto.Hashes.DoubleSHA256(Stream()).ToBytes(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] Stream()
        {
            using var ts = new Helper.TangramStream();

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
                    .Append(Prev.Data ?? Array.Empty<byte>())
                    .Append(Prev.Hash ?? string.Empty)
                    .Append(Prev.Node)
                    .Append(Prev.Round);
            }

            return ts.ToArray();
        }
    }
}