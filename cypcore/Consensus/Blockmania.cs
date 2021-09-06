// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using CYPCore.Consensus.Messages;
using CYPCore.Consensus.States;
using CYPCore.Consensus.Models;
using CYPCore.Extensions;
using Serilog;

namespace CYPCore.Consensus
{
    using StateKV = Dictionary<StateData, object>;

    public class Blockmania
    {
        public class InterpretedEventArgs : EventArgs
        {
            public Interpreted Interpreted { get; }
            public ulong Round { get; }

            public InterpretedEventArgs(Interpreted interpreted)
            {
                Interpreted = interpreted;
                Round = Interpreted.Round;
            }
        }

        private readonly ILogger _logger;
        private readonly Mutex _graphMutex;
        private readonly Channel<BlockGraph> _entries;

        public readonly IObservable<EventPattern<InterpretedEventArgs>> TrackingDelivered;

        protected virtual void OnBlockmaniaDelivered(InterpretedEventArgs e)
        {
            DeliveredEventHandler?.Invoke(this, e);
        }

        public event EventHandler<InterpretedEventArgs> DeliveredEventHandler;

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

        public Blockmania(ILogger logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(Blockmania));
            Blocks = new List<BlockInfo>();
            Max = new Dictionary<Block, ulong>();
            Resolved = new Dictionary<ulong, Dictionary<ulong, string>>();
            Statess = new Dictionary<Block, State>();
            _graphMutex = new Mutex();
            Consensus = new List<Reached>();
        }

        public Blockmania(Config cfg, ILogger logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(Blockmania));
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


            TrackingDelivered = Observable.FromEventPattern<InterpretedEventArgs>(
                ev => DeliveredEventHandler += ev, ev => DeliveredEventHandler -= ev);


            _logger.Here().Debug("Blockmania configuration: {@Self}, {@Round}, {@NodeCount}, {@Nodes}, {@TotalNodes}, {@f}, {@Quorumf1}, {@Quorum2f}, {@Quorum2f1}",
                Self, Round, NodeCount, Nodes, TotalNodes,
                f, Quorumf1, Quorum2f, Quorum2f1);

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
            _logger.Here().Debug("Deliver {@Node}, {@Round}, {@Hash}", node, round, hash);
            if (round < Round)
            {
                _logger.Here().Debug("Abort deliver, {@DeliveredRound} < {@Round}", round, Round);
                return;
            }

            Dictionary<ulong, string> hashes;

            if (Resolved.ContainsKey(round))
            {
                _logger.Here().Debug("Resolved contains key {@Round}", round);
                hashes = Resolved[round];
                if (hashes.ContainsKey(node))
                {
                    _logger.Here().Debug("Hashes contains key {@Node}", node);
                    var curHash = hashes[node];
                    if (curHash != hash)
                    {
                        _logger.Here().Debug("Mismatching block hash for delivery, node: {@Node}, round: {Round}",
                            node, round);
                    }
                }
                else
                {
                    _logger.Here().Information("Consensus achieved, hash: {@Hash}, node: {@Node}, round: {@Round}",
                        hash, node, round);

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

                _logger.Here().Debug("Adding {@Hashes} to Resolved[{@Round}]", hashes, round);
                Resolved[round] = hashes;
            }

            if (round == Round)
            {
                _logger.Here().Debug("Number of hashes: {@HashesCount}, number of nodes: {@NodeCount}",
                    hashes.Count, NodeCount);

                if (hashes.Count != NodeCount) return;

                _logger.Here().Debug("Finalize deliver round: {@DeliveredRound}, hashes: {@Hashes}",
                    round, hashes);

                DeliverRound(round, hashes);
                return;
            }

            _logger.Here().Debug("Stop deliver, {@DeliveredRound} != {@Round}", round, Round);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="round"></param>
        /// <param name="hashes"></param>
        private void DeliverRound(ulong round, Dictionary<ulong, string> hashes)
        {
            _logger.Here().Debug("DeliverRound {@Resolved}", Resolved);

            while (true)
            {
                _logger.Here().Debug("Round: {@Round}, Hashes: @{Hashes}", round, hashes);

                var blocks = (from item in hashes
                              where !string.IsNullOrEmpty(item.Value)
                              select new Block(item.Value, item.Key, round)).ToList();

                blocks.Sort((x, y) => string.Compare(x.Hash, y.Hash, StringComparison.Ordinal));
                Resolved.Remove(round);

                _logger.Here().Debug("Sorted blocks: {@Blocks}", blocks);

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

                _logger.Here().Debug("Mem usage: g.max: {@MaxCount} g.states={@StatesCount} g.blocks={@BlocksCount}",
                    Max.Count, Statess.Count, Blocks.Count);

                _graphMutex.ReleaseMutex();

                OnBlockmaniaDelivered(new InterpretedEventArgs(new Interpreted(blocks, consumed, round)));

                _logger.Here().Debug("Resolved: {@Resolved}, {@HashCound}, {@NodeCount}",
                    Resolved, hashes.Count, NodeCount);

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
            _logger.Here().Debug("Interpreting block block.id: {@BlockID}", entry.Block);

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
                _logger.Here().Debug("Processing block dep block.id: {@BlockID}", dep);

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
            _logger.Here().Debug("State: {@State}, Sender: {@Sender}, Receiver: {@Receiver}, Origin: {@Origin}, Message: {@Message}",
                s, sender.ToString(), receiver.ToString(), origin, msg);

            var (node, round) = msg.NodeRound();
            if (s.Data.ContainsKey(new Final(node, round)))
            {
                _logger.Here().Debug("State contains final, node: {@Node}, round: {@Round}",
                    node, round);

                return null;
            }
            var v = s.GetView(node, round);

            _logger.Here().Debug("Processing message from block block.id: {@BlockID}, message: {@Message}",
                origin, msg);

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

                    _logger.Here().Debug("PrePrepare, node: {@Node}, round: {@Round}, sender: {@Sender}, hash: {@Hash}",
                        node.ToString(), round.ToString(), receiver.ToString(), m.Hash);

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

                    _logger.Here().Debug("Prepare count: {@PrepareCount}", b.PrepareCount());

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

                    _logger.Here().Debug("Prepare, node: {@Node}, round: {@Round}, sender: {@Sender}, hash: {@Hash}",
                        node.ToString(), round.ToString(), receiver.ToString(), m.Hash);

                    return new Commit(m.Hash, node, round, receiver, m.View);
                case Commit m:
                    if (v < m.View)
                    {
                        return null;
                    }
                    // b = s.GetBitSet(NodeCount, m.Pre());
                    b = s.GetBitSet((int)sender, m.Pre());
                    b.SetCommit(m.Sender);

                    _logger.Here().Debug("Commit count: {@CommitCount}", b.CommitCount());

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

                    _logger.Here().Debug("Deliver, node: {@Node}, round: {@Round}, hash: {@Hash}",
                        node.ToString(), round.ToString(), m.Hash);

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
                            _logger.Here().Debug("Got multiple hashes in a view change node.id: {@Node}, round: {Round}, hash={@Hash} hash.alt={@HashAlt}",
                                node.ToString(), round.ToString(), hash, hval);
                        }
                        hash = hval;
                    }

                    _logger.Here().Debug("ViewChange, node: {@Node}, round: {@Round}, sender: {@Sender}, hash: {@Hash}",
                        node.ToString(), round.ToString(), receiver.ToString(), m.Hash);

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

                    _logger.Here().Debug("NewView -> PrePrepare, node: {@Node}, round: {@Round}, hash: {@Hash}",
                        node.ToString(), round.ToString(), m.Hash);

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

                        _logger.Here().Debug("Dep: block.id: {@BlockID}", dep.Block);

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
            _logger.Here().Debug("Adding block to graph block.id: {@BlockId}", data.Block);
            Task.Factory.StartNew(async () => { await _entries.Writer.WriteAsync(data); });
        }
    }
}
