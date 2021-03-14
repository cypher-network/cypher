// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Collections.Generic;
using CYPCore.Consensus.Models;
using FlatSharp.Attributes;


namespace CYPCore.Models
{
    public interface IStagingProto
    {
        string Hash { get; set; }
        ulong Node { get; set; }
        IList<ulong> Nodes { get; set; }
        IList<ulong> WaitingOn { get; set; }
        int TotalNodes { get; set; }
        int ExpectedTotalNodes { get; set; }
        StagingState Status { get; set; }
        IList<BlockGraph> BlockGraphs { get; set; }
        long Epoch { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        byte[] ToIdentifier();
    }

    [FlatBufferTable]
    public class StagingProto : object, IStagingProto
    {
        public static StagingProto CreateInstance()
        {
            return new();
        }

        [FlatBufferItem(0)] public virtual string Hash { get; set; }
        [FlatBufferItem(1)] public virtual ulong Node { get; set; }
        [FlatBufferItem(2)] public virtual IList<ulong> Nodes { get; set; } = new List<ulong>();
        [FlatBufferItem(3)] public virtual IList<ulong> WaitingOn { get; set; } = new List<ulong>();
        [FlatBufferItem(4)] public virtual int TotalNodes { get; set; }
        [FlatBufferItem(5)] public virtual int ExpectedTotalNodes { get; set; }
        [FlatBufferItem(6, DefaultValue = StagingState.None)] public virtual StagingState Status { get; set; }
        [FlatBufferItem(7)] public virtual IList<BlockGraph> BlockGraphs { get; set; } = new List<BlockGraph>();
        [FlatBufferItem(8)] public virtual long Epoch { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToIdentifier()
        {
            return NBitcoin.Crypto.Hashes.DoubleSHA256(Stream()).ToBytes(false);
        }

        public byte[] Stream()
        {
            using var ts = new Helper.TangramStream();
            ts
                .Append(Hash)
                .Append(Node);

            foreach (var @ulong in Nodes)
            {
                ts.Append(@ulong);
            }

            foreach (var @ulong in WaitingOn)
            {
                ts.Append(@ulong);
            }

            ts
                .Append(TotalNodes)
                .Append(ExpectedTotalNodes)
                .Append(Status.ToString());

            foreach (var blockGraph in BlockGraphs)
            {
                ts.Append(blockGraph.ToIdentifier());
            }

            return ts.ToArray(); ;
        }
    }
}
