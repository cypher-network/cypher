// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using CYPCore.Consensus.Models;
using Newtonsoft.Json;
using ProtoBuf;
using CYPCore.Extentions;

namespace CYPCore.Models
{
    public interface IMemPoolProto
    {
        InterpretedProto Block { get; set; }
        List<DepProto> Deps { get; set; }
        InterpretedProto Prev { get; set; }

        bool Equals(object obj);
        bool Equals(MemPoolProto other);
        int GetHashCode();
        BlockGraph ToBlockGraph();
        byte[] ToIdentifier();
    }

    [ProtoContract]
    public class MemPoolProto : IEquatable<MemPoolProto>, IMemPoolProto
    {
        public static MemPoolProto CreateInstance()
        {
            return new MemPoolProto();
        }

        [ProtoMember(1)] public InterpretedProto Block { get; set; } = InterpretedProto.CreateInstance();
        [ProtoMember(2)] public List<DepProto> Deps { get; set; } = new List<DepProto>();
        [ProtoMember(3)] public InterpretedProto Prev { get; set; } = InterpretedProto.CreateInstance();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public BlockGraph ToBlockGraph()
        {
            var blockGraph = Prev == null
                ? new BlockGraph(new BlockId(Block.Hash, Block.Node, Block.Round, Block.Transaction))
                : new BlockGraph(new BlockId(Block.Hash, Block.Node, Block.Round, Block.Transaction),
                    new BlockId(Prev.Hash, Prev.Node, Prev.Round, Prev.Transaction));

            foreach (var dep in Deps)
            {
                var dependencies = dep.Deps.Select(d => new BlockId(d.Hash, d.Node, d.Round, d.Transaction)).ToList();

                if (dep.Prev != null)
                {
                    blockGraph.Deps.Add(
                        new Dep(
                            new BlockId(dep.Block.Hash, dep.Block.Node, dep.Block.Round, dep.Block.Transaction),
                            dependencies,
                            new BlockId(dep.Prev.Hash, dep.Prev.Node, dep.Prev.Round, dep.Prev.Transaction))
                    );
                }
                else
                {
                    blockGraph.Deps.Add(
                        new Dep(
                            new BlockId(dep.Block.Hash, dep.Block.Node, dep.Block.Round, dep.Block.Transaction),
                            dependencies)
                    );
                }
            }

            return blockGraph;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToIdentifier()
        {
            return Block.ToHash().ByteToHex().ToBytes();
        }

        public static bool operator ==(MemPoolProto left, MemPoolProto right) => Equals(left, right);

        public static bool operator !=(MemPoolProto left, MemPoolProto right) => !Equals(left, right);

        public override bool Equals(object obj) => (obj is MemPoolProto blockGraph) && Equals(blockGraph);

        public bool Equals(MemPoolProto other)
        {
            return (Block.Hash, Block.Node, Block.Round, Deps.Count) == (other.Block.Hash, other.Block.Node,
                other.Block.Round, other.Deps.Count);
        }

        public override int GetHashCode() => base.GetHashCode();

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