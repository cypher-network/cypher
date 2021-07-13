// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Blake3;
using CYPCore.Extensions;
using MessagePack;
using Validator = CYPCore.Ledger.Validator;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class Block
    {
        [MessagePack.Key(0)] public byte[] Hash { get; set; }
        [MessagePack.Key(1)] public ulong Height { get; set; }
        [MessagePack.Key(2)] public ushort Size { get; set; }
        [MessagePack.Key(3)] public BlockHeader BlockHeader { get; set; }
        [MessagePack.Key(4)] public ushort NrTx { get; set; }
        [MessagePack.Key(5)] public IList<Transaction> Txs { get; set; } = new List<Transaction>();
        [MessagePack.Key(6)] public BlockPos BlockPos { get; set; }

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
        public byte[] ToIdentifier()
        {
            return ToHash().ByteToHex().ToBytes();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToStream()
        {
            if (Validate().Any()) return null;

            using var ts = new Helper.TangramStream();
            ts.Append(Height);
            if (Size != 0)
            {
                ts.Append(Size);
            }

            ts.Append(BlockHeader.ToStream())
                .Append(NrTx)
                .Append(Helper.Util.Combine(Txs.Select(x => x.ToHash()).ToArray()))
                .Append(BlockPos.ToStream());
            return ts.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public ushort GetSize()
        {
            return (ushort)ToStream().Length;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();
            if (Hash == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "Hash" }));
            }
            if (Hash != null && Hash.Length != 32)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Hash" }));
            }
            if (Height < 0)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Height" }));
            }
            if (Size <= 0)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Size" }));
            }
            if (Size > 65_535)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Size" }));
            }

            results.AddRange(BlockHeader.Validate());

            if (NrTx > 65_535)
            {
                results.Add(new ValidationResult("Range exception", new[] { "NrTx" }));
            }
            if (!BlockHeader.MerkleRoot.Xor(Validator.BlockZeroMerkel) &&
                !BlockHeader.PrevBlockHash.Xor(Hasher.Hash(Validator.BlockZeroPreHash).HexToByte()))
            {
                foreach (var transaction in Txs)
                {
                    results.AddRange(transaction.Validate());
                }
            }
            results.AddRange(BlockPos.Validate());
            return results;
        }
    }
}