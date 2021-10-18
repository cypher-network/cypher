// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Blake3;
using CYPCore.Extensions;
using Dawn;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class BlockHeader
    {
        [MessagePack.Key(0)] public uint Version { get; set; }
        [MessagePack.Key(1)] public byte[] PrevBlockHash { get; set; }
        [MessagePack.Key(2)] public byte[] MerkleRoot { get; set; }
        [MessagePack.Key(3)] public ulong Height { get; set; }
        [MessagePack.Key(4)] public long Locktime { get; set; }
        [MessagePack.Key(5)] public string LocktimeScript { get; set; }

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

            using var ts = new Helper.BufferStream();

            ts.Append(Version)
                .Append(PrevBlockHash)
                .Append(MerkleRoot)
                .Append(Height)
                .Append(Locktime)
                .Append(LocktimeScript);
            return ts.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prevMerkelRoot"></param>
        /// <param name="transactions"></param>
        /// <returns></returns>
        public static byte[] ToMerkelRoot(in byte[] prevMerkelRoot, in ImmutableArray<Transaction> transactions)
        {
            Guard.Argument(prevMerkelRoot, nameof(prevMerkelRoot)).NotNull().MaxCount(32);
            Guard.Argument(transactions, nameof(transactions)).NotEmpty();
            var hasher = Hasher.New();
            hasher.Update(prevMerkelRoot);
            foreach (var transaction in transactions)
            {
                var hasAnyErrors = transaction.Validate();
                if (hasAnyErrors.Any()) throw new ArithmeticException("Unable to validate the transaction");
                hasher.Update(transaction.ToStream());
            }

            var hash = hasher.Finalize();
            return hash.HexToByte();
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
        public IEnumerable<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();
            if (Version != 0x2)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Version" }));
            }
            if (PrevBlockHash == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "PrevBlockHash" }));
            }
            if (PrevBlockHash != null && PrevBlockHash.Length != 32)
            {
                results.Add(new ValidationResult("Range exception", new[] { "PrevBlockHash" }));
            }
            if (Height < 0)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Height" }));
            }
            if (MerkleRoot == null)
            {
                results.Add(new ValidationResult("Argument is null", new[] { "MerkleRoot" }));
            }
            if (MerkleRoot != null && MerkleRoot.Length != 32)
            {
                results.Add(new ValidationResult("Range exception", new[] { "MerkleRoot" }));
            }
            if (Locktime < 0)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Locktime" }));
            }
            try
            {
                DateTimeOffset.FromUnixTimeSeconds(Locktime);
            }
            catch (ArgumentOutOfRangeException)
            {
                results.Add(new ValidationResult("Range exception", new[] { "Locktime" }));
            }
            if (LocktimeScript != null && LocktimeScript.Length != 16)
            {
                results.Add(new ValidationResult("Range exception", new[] { "LocktimeScript" }));
            }
            return results;
        }
    }
}

