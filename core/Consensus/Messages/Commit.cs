﻿// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

namespace CypherNetwork.Consensus.Messages;

public class Commit : IMessage
{
    public Commit()
    {
    }

    public Commit(string hash, ulong node, ulong round, ulong sender, uint view)
    {
        Hash = hash;
        Node = node;
        Round = round;
        Sender = sender;
        View = view;
    }

    public string Hash { get; set; }
    public ulong Node { get; set; }
    public ulong Round { get; set; }
    public ulong Sender { get; set; }
    public uint View { get; set; }

    public MessageKind Kind()
    {
        return MessageKind.CommitMsg;
    }

    public Tuple<ulong, ulong> NodeRound()
    {
        return Tuple.Create(Node, Round);
    }

    public override string ToString()
    {
        return
            $"commit{{node: {Node}, round: {Round}, view: {View}, hash: '{Util.FmtHash(Hash):S}', sender: {Sender}}}";
    }

    public PrePrepare Pre()
    {
        return new PrePrepare(Hash, Node, Round, View);
    }
}