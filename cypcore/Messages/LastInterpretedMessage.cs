// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Models;

namespace CYPCore.Messages
{
    public class LastInterpretedMessage
    {
        public byte[] Hash { get; }
        public ulong Last { get; }
        public InterpretedProto InterpretedProto { get; }

        public LastInterpretedMessage(ulong last, InterpretedProto interpretedProto)
        {
            Last = last;
            InterpretedProto = interpretedProto;
        }

        public LastInterpretedMessage(byte[] hash, InterpretedProto interpretedProto)
        {
            Hash = hash;
            InterpretedProto = interpretedProto;
        }

        public LastInterpretedMessage(ulong last, byte[] hash, InterpretedProto interpretedProto)
        {
            Hash = hash;
            Last = last;
            InterpretedProto = interpretedProto;
        }
    }
}
