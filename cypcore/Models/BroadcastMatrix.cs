// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPCore.Network;

namespace CYPCore.Models
{
    public class BroadcastMatrix
    {
        public Peer Peer { get; set; }
        public int[] Received { get; set; }
        public int[] Sending { get; set; }
    }
}