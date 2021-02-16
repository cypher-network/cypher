// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using CYPCore.Consensus.Blockmania;
using Newtonsoft.Json;
using ProtoBuf;
using CYPCore.Extentions;

namespace CYPCore.Models
{
    [ProtoContract]
    public class MemPoolProto : IEquatable<MemPoolProto>, IMemPoolProto
    {
        public int Id { get; set; }

        [ProtoMember(1)]
        public int Included { get; set; }
        [ProtoMember(2)]
        public int Replied { get; set; }
        [ProtoMember(3)]
        public InterpretedProto Block { get; set; } = new InterpretedProto();
        [ProtoMember(4)]
        public List<DepProto> Deps { get; set; } = new List<DepProto>();
        [ProtoMember(5)]
        public InterpretedProto Prev { get; set; } = new InterpretedProto();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public BlockGraph ToMemPool()
        {
            BlockGraph blockGraph;

            if (Prev == null)
            {
                blockGraph = new BlockGraph(
                    new BlockID(Block.Hash, Block.Node, Block.Round, Block.Transaction));
            }
            else
            {
                blockGraph = new BlockGraph(
                    new BlockID(Block.Hash, Block.Node, Block.Round, Block.Transaction),
                    new BlockID(Prev.Hash, Prev.Node, Prev.Round, Prev.Transaction));
            }

            foreach (var dep in Deps)
            {
                var deps = new List<BlockID>();
                foreach (var d in dep.Deps)
                {
                    deps.Add(new BlockID(d.Hash, d.Node, d.Round, d.Transaction));
                }

                if (dep.Prev == null)
                {
                    blockGraph.Deps.Add(
                      new Dep(
                          new BlockID(dep.Block.Hash, dep.Block.Node, dep.Block.Round, dep.Block.Transaction), deps)
                    );
                }
                else
                {
                    blockGraph.Deps.Add(
                      new Dep(
                          new BlockID(dep.Block.Hash, dep.Block.Node, dep.Block.Round, dep.Block.Transaction), deps,
                          new BlockID(dep.Prev.Hash, dep.Prev.Node, dep.Prev.Round, dep.Prev.Transaction))
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lookup"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        public static IEnumerable<MemPoolProto> NextBlockGraph(ILookup<string, MemPoolProto> lookup, ulong node)
        {
            if (lookup == null)
                throw new ArgumentNullException(nameof(lookup));

            if (node < 0)
                throw new ArgumentOutOfRangeException(nameof(node));

            for (int i = 0, lookupCount = lookup.Count; i < lookupCount; i++)
            {
                var blockGraphs = lookup.ElementAt(i);
                MemPoolProto root = null;

                var sorted = CurrentNodeFirst(blockGraphs.ToList(), node);

                foreach (var next in sorted)
                {
                    if (next.Block.Node.Equals(node))
                        root = NewBlockGraph(next);
                    else
                        AddDependency(root, next);
                }

                if (root == null)
                    continue;

                yield return root;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraphs"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        private static IEnumerable<MemPoolProto> CurrentNodeFirst(List<MemPoolProto> blockGraphs, ulong node)
        {
            // Not the best solution...
            var list = new List<MemPoolProto>();
            var nodeIndex = blockGraphs.FindIndex(x => x.Block.Node.Equals(node));

            list.Add(blockGraphs[nodeIndex]);
            blockGraphs.RemoveAt(nodeIndex);
            list.AddRange(blockGraphs);

            return list;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        /// <returns></returns>
        private static MemPoolProto NewBlockGraph(MemPoolProto next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            return new MemPoolProto
            {
                Block = next.Block,
                Deps = next.Deps,
                Prev = next.Prev
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="root"></param>
        /// <param name="next"></param>
        public static void AddDependency(MemPoolProto root, MemPoolProto next)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            if (next == null)
                throw new ArgumentNullException(nameof(next));

            if (root.Deps?.Any() != true)
            {
                root.Deps = new List<DepProto>();
            }

            root.Deps.Add(new DepProto
            {
                Block = next.Block,
                Deps = next.Deps?.Select(d => d.Block).ToList(),
                Prev = next.Prev ?? null
            });
        }

        public static bool operator ==(MemPoolProto left, MemPoolProto right) => Equals(left, right);

        public static bool operator !=(MemPoolProto left, MemPoolProto right) => !Equals(left, right);

        public override bool Equals(object obj) => (obj is MemPoolProto blockGraph) && Equals(blockGraph);

        public bool Equals(MemPoolProto other)
        {
            return (Block.Hash, Block.Node, Block.Round, Deps.Count) == (other.Block.Hash, other.Block.Node, other.Block.Round, other.Deps.Count);
        }

        public override int GetHashCode() => base.GetHashCode();

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TAttach"></typeparam>
        /// <returns></returns>
        public T Cast<T>()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
