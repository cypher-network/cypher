// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using CypherNetwork.Consensus.Messages;
using CypherNetwork.Consensus.States;

namespace CypherNetwork.Consensus;

using StateKV = Dictionary<StateData, object>;

public class NodeRound : IEquatable<NodeRound>
{
    public NodeRound(ulong node, ulong round)
    {
        Node = node;
        Round = round;
    }

    public ulong Node { get; }
    public ulong Round { get; }

    public bool Equals(NodeRound other)
    {
        return other != null
               && other.Node == Node
               && other.Round == Round;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Node, Round);
    }
}

public class State
{
    public Dictionary<PrePrepare, BitSet> BitSets = new();
    public StateKV Data = new();
    public Dictionary<ulong, ulong> Delay = new();
    public Dictionary<NodeRound, string> Final = new();
    public List<IMessage> Out = new();
    public ulong Timeout;
    public Dictionary<ulong, List<Timeout>> Timeouts = new();

    public State(Dictionary<ulong, List<Timeout>> timeouts)
    {
        Timeouts = timeouts;
    }

    public State(ulong timeout)
    {
        Timeout = timeout;
    }

    public State Clone(ulong minRound)
    {
        var n = new State(Timeout);
        if (BitSets != null)
        {
            var bitsets = new Dictionary<PrePrepare, BitSet>();
            foreach (var item in BitSets)
            {
                if (item.Key.Round < minRound) continue;
                bitsets[item.Key] = item.Value.Clone();
            }

            n.BitSets = bitsets;
        }

        if (Data != null)
        {
            var data = new StateKV();
            foreach (var item in Data)
            {
                if (item.Key.GetRound() < minRound) continue;
                data[item.Key] = item.Value;
            }

            n.Data = data;
        }

        if (Delay != null)
        {
            var delay = new Dictionary<ulong, ulong>();
            foreach (var item in Delay) delay[item.Key] = item.Value;
            n.Delay = delay;
        }

        if (Final != null)
        {
            var final = new Dictionary<NodeRound, string>();
            foreach (var item in Final)
            {
                if (item.Key.Round < minRound) continue;
                final[item.Key] = item.Value;
            }

            n.Final = final;
        }

        var out_ = new List<IMessage>();
        foreach (var msg in Out)
        {
            var r = msg.NodeRound().Item2;
            if (r < minRound) continue;
            out_.Add(msg);
        }

        n.Out = out_;
        var timeouts = new Dictionary<ulong, List<Timeout>>();
        foreach (var item in Timeouts)
        {
            if (item.Key < minRound) continue;
            timeouts[item.Key] = item.Value;
        }

        n.Timeouts = timeouts;
        return n;
    }

    public BitSet GetBitSet(int size, PrePrepare pp)
    {
        if (BitSets == null)
        {
            var b = new BitSet(size);
            BitSets = new Dictionary<PrePrepare, BitSet>
            {
                [pp] = b
            };
            return b;
        }

        if (BitSets.ContainsKey(pp)) return BitSets[pp];
        var newB = new BitSet(size);
        BitSets[pp] = newB;
        return newB;
    }

    public List<IMessage> GetOutPut()
    {
        return Out;
    }

    public uint GetView(ulong node, ulong round)
    {
        var key = new View(node, round); // it's ok, IEquatable implemented
        if (Data.ContainsKey(key))
        {
            var val = Data[key];
            return (uint)val;
        }

        if (Data == null) Data = new StateKV();
        Data[key] = (uint)0;
        return 0;
    }
}

internal class Util
{
    public static ulong Diff(ulong a, ulong b)
    {
        if (a >= b) return a - b;
        return b - a;
    }
}

public class Timeout
{
    public ulong Node;
    public ulong Round;
    public uint View;

    public Timeout(ulong node, ulong round, uint view)
    {
        Node = node;
        Round = round;
        View = view;
    }
}