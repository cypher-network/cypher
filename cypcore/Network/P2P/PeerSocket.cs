// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Models;

namespace CYPCore.Network.P2P
{
    public class PeerSocket
    {
        public SocketTopicType TopicType { get; set; }
        public string WSAddress { get; set; }
    }
}
