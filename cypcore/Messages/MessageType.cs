// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Messages
{
    public class MessageType
    {
        private readonly string _name;
        private readonly int _value;

        public static readonly MessageType BlockGraph = new MessageType(1, "blockgraph");
        public static readonly MessageType BlockChain = new MessageType(2, "blockchain");
        public static readonly MessageType OneKeyImage = new MessageType(3, "onekeyimage");
        public static readonly MessageType RingMembersExist = new MessageType(4, "ringmembersexist");

        public int Value => _value;

        private MessageType(int value, string name)
        {
            _value = value;
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }
    }
}
