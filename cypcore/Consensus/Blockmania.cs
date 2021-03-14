// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Diagnostics;
using System.Linq;
using CYPCore.Consensus.Messages;
using CYPCore.Consensus.States;
using CYPCore.Consensus.Models;

namespace CYPCore.Consensus
{
    using StateKV = Dictionary<StateData, object>;

    public class Blockmania
    {
        private readonly Mutex _graphMutex;
        private readonly Channel<BlockGraph> _entries;

        protected virtual void OnBlockmaniaInterpreted(Interpreted e)
        {
            Delivered?.Invoke(this, e);
        }

        public event EventHandler<Interpreted> Delivered;

        public List<BlockInfo> Blocks;
        public Func<Task<Interpreted>> Action;
        public Dictionary<Block, ulong> Max;
        public int NodeCount;
        public ulong[] Nodes;
        public int Quorumf1;
        public int Quorum2f;
        public int Quorum2f1;
        public Dictionary<ulong, Dictionary<ulong, string>> Resolved;
        public ulong Round;
        public ulong Self;
        public Dictionary<Block, State> Statess;
        public ulong TotalNodes;
        public List<Reached> Consensus;

        public Blockmania()
        {
            Blocks = new List<BlockInfo>();
            Max = new Dictionary<Block, ulong>();
            Resolved = new Dictionary<ulong, Dictionary<ulong, string>>();
            Statess = new Dictionary<Block, State>();
            _graphMutex = new Mutex();
            Consensus = new List<Reached>();
        }

        public Blockmania(Config cfg)
        {
            var f = (cfg.Nodes.Length - 1) / 3;
            Blocks = new List<BlockInfo>();
            Max = new Dictionary<Block, ulong>();
            NodeCount = cfg.Nodes.Length;
            Nodes = cfg.Nodes;
            Quorumf1 = f + 1;
            Quorum2f = 2 * f;
            Quorum2f1 = 2 * f + 1;
            Resolved = new Dictionary<ulong, Dictionary<ulong, string>>();
            Round = cfg.LastInterpreted + 1;
            Self = cfg.SelfId;
            Statess = new Dictionary<Block, State>();
            TotalNodes = cfg.TotalNodes;
            _graphMutex = new Mutex();
            _entries = Channel.CreateBounded<BlockGraph>(10000);
            Consensus = new List<Reached>();

            _ = Task.Factory.StartNew(async () =>
            {
                await Run(_entries.Reader);
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="round"></param>
        /// <param name="hash"></param>
        private void Deliver(ulong node, ulong round, string hash)
        {
            if (round < Round)
            {
                return;
            }

            Dictionary<ulong, string> hashes;

            if (Resolved.ContainsKey(round))
            {
                hashes = Resolved[round];
                if (hashes.ContainsKey(node))
                {
                    var curHash = hashes[node];
                    if (curHash != hash)
                    {
                        Debug.WriteLine($"Mismatching block hash for delivery, node={node} round={round}");
                    }
                }
                else
                {
                    Console.WriteLine($"Consensus achieved hash={hash} node={node} round={round}");
                    Consensus.Add(new Reached(hash, node, round));
                    hashes[node] = hash;
                }
            }
            else
            {
                hashes = new Dictionary<ulong, string>
                {
                    {node, hash}
                };

                Resolved[round] = hashes;
            }
            if (round != Round)
            {
                return;
            }
            if (hashes.Count == NodeCount)
            {
                DeliverRound(round, hashes);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="round"></param>
        /// <param name="hashes"></param>
        private void DeliverRound(ulong round, Dictionary<ulong, string> hashes)
        {
            while (true)
            {
                var blocks = (from item in hashes
                              where !string.IsNullOrEmpty(item.Value)
                              select new Block(item.Value, item.Key, round)).ToList();

                blocks.Sort((x, y) => string.Compare(x.Hash, y.Hash, StringComparison.Ordinal));
                Resolved.Remove(round);

                _graphMutex.WaitOne();

                var consumed = Blocks[0].Data.Block.Round - 1;
                var idx = 0;
                for (var i = 0; i < Blocks.Count; i++)
                {
                    var info = Blocks[i];

                    var b = blocks.Find(x =>
                        x.Hash == info.Data.Block.Hash && x.Node == info.Data.Block.Node &&
                        x.Round == info.Data.Block.Round);

                    if (b != null)
                    {
                        b.Data = info.Data.Block.Data;
                    }

                    if (info.Max > round)
                    {
                        break;
                    }

                    Max.Remove(info.Data.Block);
                    Statess.Remove(info.Data.Block);
                    foreach (var dep in info.Data.Deps)
                    {
                        Max.Remove(dep.Block);
                        Statess.Remove(dep.Block);
                    }

                    consumed++;
                    idx = i + 1;
                }

                if (idx > 0)
                {
                    Blocks = Blocks.GetRange(idx, Blocks.Count - idx);
                }

                Round++;

                Debug.WriteLine($"Mem usage: g.max={Max.Count} g.states={Statess.Count} g.blocks={Blocks.Count}");
                _graphMutex.ReleaseMutex();

                OnBlockmaniaInterpreted(new Interpreted(blocks, consumed, round));

                if (!Resolved.ContainsKey(round + 1) || hashes.Count != NodeCount) return;

                hashes = Resolved[round + 1];
                round += 1;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private State FindOrCreateState(Entry e)
        {
            State stat;
            if (e.Prev.Valid())
            {
                stat = !Statess.ContainsKey(e.Prev) ? new State(new Dictionary<ulong, List<Timeout>>()).Clone(Round) : Statess[e.Prev].Clone(Round);
            }
            else
            {
                stat = new State(new Dictionary<ulong, List<Timeout>>());
            }
            return stat;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entry"></param>
        private void Process(Entry entry)
        {
            Debug.WriteLine($"Interpreting block block.id={entry.Block}");

            var state = FindOrCreateState(entry);
            var node = entry.Block.Node;
            var round = entry.Block.Round;
            var hash = entry.Block.Hash;
            var outMessages = new List<IMessage>
            {
                new PrePrepare(hash, node, round, 0)
            };

            if (entry.Deps.Any())
            {
                state.Delay ??= new Dictionary<ulong, ulong>();

                foreach (var dep in entry.Deps)
                {
                    state.Delay[dep.Node] = Util.Diff(round, dep.Round) * 10;
                }
            }

            var timeOutVal = 10UL;
            if (state.Delay.Count > Quorum2f1)
            {
                var timeOutValues = new ulong[state.Delay.Count];
                var i = 0;
                foreach (var item in state.Delay)
                {
                    timeOutValues[i] = item.Value;
                    i++;
                }
                Array.Sort(timeOutValues);

                var xval = timeOutValues[Quorum2f];
                if (xval > timeOutVal)
                {
                    timeOutVal = xval;
                }
            }
            else
            {
                foreach (var val in state.Delay.Select(item => item.Value).Where(val => val > timeOutVal))
                {
                    timeOutVal = val;
                }
            }
            state.Timeout = timeOutVal;

            var timeoutRound = round + timeOutVal;
            foreach (var nextNode in Nodes)
            {
                if (!state.Timeouts.ContainsKey(timeoutRound) || state.Timeouts[timeoutRound] == null)
                {
                    state.Timeouts[timeoutRound] = new List<Timeout>();
                }

                state.Timeouts[timeoutRound].Add(new Timeout(nextNode, round, 0));
            }

            if (state.Timeouts.ContainsKey(round))
            {
                foreach (var tmout in state.Timeouts[round])
                {
                    if (state.Data.ContainsKey(new Final(tmout.Node, tmout.Round)))
                    {
                        continue;
                    }
                    uint v = 0;
                    var skey = new View(tmout.Node, tmout.Round);
                    if (state.Data.ContainsKey(skey))
                    {
                        v = (uint)state.Data[skey];
                    }
                    if (v > tmout.View)
                    {
                        continue;
                    }
                    var hval = "";
                    var skey2 = new Prepared(tmout.Node, tmout.Round, tmout.View);
                    if (state.Data.ContainsKey(skey2))
                    {
                        hval = (string)state.Data[skey2];
                    }
                    if (state.Data == null)
                    {
                        state.Data = new StateKV { { skey, v + 1 } };
                    }
                    else
                    {
                        state.Data[skey] = v + 1;
                    }
                    outMessages.Add(new ViewChange(hval, tmout.Node, tmout.Round, node, tmout.View + 1));
                }
            }

            var idx = outMessages.Count;
            var processed = new Dictionary<IMessage, bool>();
            outMessages.AddRange(ProcessMessages(state, processed, node, node, entry.Block, outMessages.GetRange(0, idx)));
            foreach (var dep in entry.Deps)
            {
                Debug.WriteLine($"Processing block dep block.id={dep}");

                var output = new List<IMessage>();
                if (Statess.ContainsKey(dep) && Statess[dep] != null)
                {
                    output = Statess[dep].GetOutPut();
                }
                outMessages.AddRange(ProcessMessages(state, processed, dep.Node, node, entry.Block, output));
            }
            state.Out = outMessages;
            Statess[entry.Block] = state;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="s"></param>
        /// <param name="sender"></param>
        /// <param name="receiver"></param>
        /// <param name="origin"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private IMessage ProcessMessage(State s, ulong sender, ulong receiver, Block origin, IMessage msg)
        {
            var (node, round) = msg.NodeRound();
            if (s.Data.ContainsKey(new Final(node, round)))
            {
                return null;
            }
            var v = s.GetView(node, round);

            Debug.WriteLine($"Processing message from block block.id={origin} message={msg}");

            switch (msg)
            {
                case PrePrepare m:
                    if (v != m.View)
                    {
                        return null;
                    }
                    var pp = new PrePrepared(node, round, m.View);
                    if (s.Data.ContainsKey(pp))
                    {
                        return null;
                    }
                    var size = sender > receiver ? sender : receiver;
                    // var b = s.GetBitSet(NodeCount, m);
                    var b = s.GetBitSet((int)size, m);
                    b.SetPrepare(sender);
                    b.SetPrepare(receiver);
                    if (s.Data == null)
                    {
                        s.Data = new StateKV { { pp, m } };
                    }
                    else
                    {
                        s.Data[pp] = m;
                    }
                    return new Prepare(m.Hash, node, round, receiver, m.View);
                case Prepare m:
                    if (v > m.View)
                    {
                        return null;
                    }
                    if (v < m.View)
                    {
                        // b = s.GetBitSet(NodeCount, m.Pre());
                        b = s.GetBitSet((int)sender, m.Pre());
                        b.SetPrepare(m.Sender);
                        return null;
                    }
                    // b = s.GetBitSet(NodeCount, m.Pre());
                    b = s.GetBitSet((int)sender, m.Pre());
                    b.SetPrepare(m.Sender);

                    Debug.WriteLine($"Prepare count == {b.PrepareCount()}");

                    if (b.PrepareCount() != Quorum2f1)
                    {
                        return null;
                    }
                    if (b.HasCommit(receiver))
                    {
                        return null;
                    }
                    b.SetCommit(receiver);
                    var p = new Prepared(node, round, m.View);
                    if (s.Data.ContainsKey(p)) return new Commit(m.Hash, node, round, receiver, m.View);
                    if (s.Data == null)
                    {
                        s.Data = new StateKV() { { p, m.Hash } };
                    }
                    else
                    {
                        s.Data[p] = m.Hash;
                    }
                    return new Commit(m.Hash, node, round, receiver, m.View);
                case Commit m:
                    if (v < m.View)
                    {
                        return null;
                    }
                    // b = s.GetBitSet(NodeCount, m.Pre());
                    b = s.GetBitSet((int)sender, m.Pre());
                    b.SetCommit(m.Sender);

                    Debug.WriteLine($"Commit count == {b.CommitCount()}");

                    if (b.CommitCount() != Quorum2f1)
                    {
                        return null;
                    }
                    var nr = new NodeRound(node, round);
                    if (s.Final.ContainsKey(nr))
                    {
                        return null;
                    }
                    if (s.Final == null)
                    {
                        s.Final = new Dictionary<NodeRound, string>() { { nr, m.Hash } };
                    }
                    else
                    {
                        s.Final[nr] = m.Hash;
                    }
                    Deliver(node, round, m.Hash);
                    return null;
                case ViewChange m:
                    if (v > m.View)
                    {
                        return null;
                    }
                    Dictionary<ulong, string> vcs;
                    var key = new ViewChanged(node, round, v);
                    if (s.Data.ContainsKey(key))
                    {
                        var val = s.Data[key];
                        vcs = (Dictionary<ulong, string>)val;
                    }
                    else
                    {
                        vcs = new Dictionary<ulong, string>();
                        if (s.Data == null)
                        {
                            s.Data = new StateKV() { { key, vcs } };
                        }
                        else
                        {
                            s.Data[key] = vcs;
                        }
                    }
                    vcs[m.Sender] = m.Hash;
                    if (vcs.Count != Quorum2f1)
                    {
                        return null;
                    }
                    s.Data[new View(node, round)] = m.View;
                    var hash = "";
                    foreach (var hval in vcs.Select(item => item.Value).Where(hval => hval != ""))
                    {
                        if (hash != "" && hval != hash)
                        {
                            Console.WriteLine($"Got multiple hashes in a view change node.id={node} round={round} hash={hash} hash.alt={hval}");
                        }
                        hash = hval;
                    }
                    return new NewView(hash, node, round, receiver, m.View);
                case NewView m:
                    if (v > m.View)
                    {
                        return null;
                    }
                    var viewKey = new Hnv(node, round, m.View);
                    if (s.Data.ContainsKey(viewKey))
                    {
                        return null;
                    }
                    s.Data ??= new StateKV();
                    s.Data[new View(node, round)] = m.View;
                    var tval = origin.Round + s.Timeout + 5;
                    if (!s.Timeouts.ContainsKey(tval))
                    {
                        s.Timeouts[tval] = new List<Timeout>();
                    }
                    s.Timeouts[tval].Add(new Timeout(node, round, m.View));
                    s.Data[viewKey] = true;
                    return new PrePrepare(m.Hash, node, round, m.View);
                default:
                    throw new Exception($"blockmania: unknown message kind to process: {msg.Kind()}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="s"></param>
        /// <param name="processed"></param>
        /// <param name="sender"></param>
        /// <param name="receiver"></param>
        /// <param name="origin"></param>
        /// <param name="messages"></param>
        /// <returns></returns>
        private IEnumerable<IMessage> ProcessMessages(State s, IDictionary<IMessage, bool> processed, ulong sender,
            ulong receiver, Block origin, IEnumerable<IMessage> messages)
        {
            var outMessages = new List<IMessage>();
            foreach (var msg in messages)
            {
                if (processed.ContainsKey(msg) && processed[msg])
                {
                    continue;
                }
                var resp = ProcessMessage(s, sender, receiver, origin, msg);
                processed[msg] = true;
                if (resp != null)
                {
                    outMessages.Add(resp);
                }
            }
            for (var i = 0; i < outMessages.Count; i++)
            {
                var msg = outMessages[i];
                if (processed.ContainsKey(msg) && processed[msg])
                {
                    continue;
                }
                var resp = ProcessMessage(s, sender, receiver, origin, msg);
                processed[msg] = true;
                if (resp != null)
                {
                    outMessages.Add(resp);
                }
            }
            return outMessages;
        }

        private async Task Run(ChannelReader<BlockGraph> reader)
        {
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var data))
                {
                    _graphMutex.WaitOne();

                    var entries = new Entry[data.Deps.Count];
                    var max = data.Block.Round;
                    var round = Round;

                    for (var i = 0; i < data.Deps.Count; i++)
                    {
                        var dep = data.Deps[i];

                        Debug.WriteLine($"Dep: block.id={dep.Block}");

                        var depMax = dep.Block.Round;
                        var firstRecheck = false;
                        var entry = new Entry(dep.Block, dep.Prev);

                        if (dep.Block.Round != 1)
                        {
                            entry.Deps = new Block[dep.Deps.Count + 1];
                            entry.Deps[0] = dep.Prev;
                            Array.Copy(dep.Deps.ToArray(), 0, entry.Deps.ToArray(), 1, dep.Deps.Count);
                            var prevMax = 0ul;
                            if (Max.ContainsKey(dep.Prev))
                            {
                                prevMax = Max[dep.Prev];
                                firstRecheck = true;
                            }
                            else if (prevMax > depMax)
                            {
                                depMax = prevMax;
                            }
                        }
                        else
                        {
                            entry.Deps = dep.Deps.ToArray();
                        }
                        entries[i] = entry;
                        foreach (var link in dep.Deps)
                        {
                            if (!Max.ContainsKey(link))
                            {
                                firstRecheck = true;
                            }
                            else
                            {
                                var linkMax = Max[link];
                                if (linkMax > depMax)
                                {
                                    depMax = linkMax;
                                }
                            }
                        }
                        if (firstRecheck && round > depMax)
                        {
                            depMax = round;
                        }
                        Max[dep.Block] = depMax;
                        if (depMax > max)
                        {
                            max = depMax;
                        }
                    }
                    var secondRecheck = false;
                    if (data.Block.Round != 1)
                    {
                        if (!Max.ContainsKey(data.Prev))
                        {
                            secondRecheck = true;
                        }
                        else
                        {
                            var pmax = Max[data.Prev];
                            if (pmax > max)
                            {
                                max = pmax;
                            }
                        }
                    }
                    if (secondRecheck && round > max)
                    {
                        max = round;
                    }
                    Max[data.Block] = max;
                    Blocks.Add(new BlockInfo(data, max));
                    _graphMutex.ReleaseMutex();
                    foreach (var e in entries)
                    {
                        Process(e);
                    }
                    var self = new Entry(data.Block, data.Prev)
                    {
                        Deps = new Block[data.Deps.Count + 1]
                    };
                    self.Deps[0] = data.Prev;
                    for (var i = 0; i < data.Deps.Count; i++)
                    {
                        var dep = data.Deps[i];
                        self.Deps[i + 1] = dep.Block;
                    }
                    Process(self);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        public void Add(BlockGraph data)
        {
            Debug.WriteLine($"Adding block to graph block.id={data.Block}");

            var task = Task.Factory.StartNew(async () =>
            {
                await _entries.Writer.WriteAsync(data);
            });

            task.Wait();
        }
    }
}
