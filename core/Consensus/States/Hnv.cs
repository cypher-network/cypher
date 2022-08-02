// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Consensus.States;

public class Hnv : StateData
{
    public Hnv()
    {
    }

    public Hnv(ulong node, ulong round, uint view)
    {
        Node = node;
        Round = round;
        View = view;
    }

    public ulong Node { get; set; }
    public ulong Round { get; set; }
    public uint View { get; set; }

    public ulong GetRound()
    {
        return Round;
    }

    public StateDataKind SdKind()
    {
        return StateDataKind.HNVState;
    }
}