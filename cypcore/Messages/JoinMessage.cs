// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Serf;

namespace CYPCore.Messages
{
    public class JoinMessage
    {
        public uint Peers { get; }
        public SerfError SerfError { get; }

        public JoinMessage(uint peers, SerfError serfError)
        {
            Peers = peers;
            SerfError = serfError;
        }
    }
}
