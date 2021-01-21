// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Consensus.BlockMania.States
{
    public class Hnv : StateData
    {
        public ulong Node { get; }
        public ulong Round { get; }
        public uint View { get; }

        public Hnv(ulong node, ulong round, uint view)
        {
            Node = node;
            Round = round;
            View = view;
        }

        public ulong GetRound()
        {
            return Round;
        }

        public StateDataKind SdKind()
        {
            return StateDataKind.HNVState;
        }
    }
}
