// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Consensus.States;

public class Final : StateData
{
    public Final()
    {
    }

    public Final(ulong node, ulong round)
    {
        Node = node;
        Round = round;
    }

    public ulong Node { get; set; }
    public ulong Round { get; set; }

    public ulong GetRound()
    {
        return Round;
    }

    public StateDataKind SdKind()
    {
        return StateDataKind.FinalState;
    }
}