// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using WebSocketSharp;
using CYPCore.Models;

namespace CYPCore.Network.P2P
{
    public class PeerSocket
    {
        public SocketTopicType TopicType { get; set; }
        public WebSocket Socket { get; set; }
    }
}
