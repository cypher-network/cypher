// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using ProtoBuf;
using CYPCore.Extentions;

namespace CYPCore.Models
{
    public interface IStagingProto
    {
        int Id { get; set; }
        string Hash { get; set; }
        ulong Node { get; set; }
        List<ulong> Nodes { get; set; }
        List<ulong> WaitingOn { get; set; }
        int TotalNodes { get; set; }
        int ExpectedTotalNodes { get; set; }
        StagingState Status { get; set; }
        MemPoolProto MemPoolProto { get; set; }
        long Epoch { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        byte[] ToIdentifier();
    }

    [ProtoContract]
    public class StagingProto : IStagingProto
    {
        public static StagingProto CreateInstance()
        {
            return new();
        }

        public int Id { get; set; }

        [ProtoMember(1)]
        public string Hash { get; set; }
        [ProtoMember(2)]
        public ulong Node { get; set; }
        [ProtoMember(3)]
        public List<ulong> Nodes { get; set; } = new();
        [ProtoMember(4)]
        public List<ulong> WaitingOn { get; set; } = new();
        [ProtoMember(5)]
        public int TotalNodes { get; set; }
        [ProtoMember(6)]
        public int ExpectedTotalNodes { get; set; }
        [ProtoMember(7)]
        public StagingState Status { get; set; }
        [ProtoMember(8)]
        public MemPoolProto MemPoolProto { get; set; }
        [ProtoMember(9)]
        public long Epoch { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToIdentifier()
        {
            return MemPoolProto.Block.ToHash().ByteToHex().ToBytes();
        }

    }
}
