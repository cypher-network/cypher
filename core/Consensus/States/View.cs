﻿// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

namespace CypherNetwork.Consensus.States;

public class View : StateData, IEquatable<View>
{
    public View()
    {
    }

    public View(ulong node, ulong round)
    {
        Node = node;
        Round = round;
    }

    public ulong Node { get; set; }
    public ulong Round { get; set; }

    public bool Equals(View other)
    {
        return other != null
               && other.Node == Node
               && other.Round == Round;
    }

    public ulong GetRound()
    {
        return Round;
    }

    public StateDataKind SdKind()
    {
        return StateDataKind.ViewState;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Node, Round);
    }
}