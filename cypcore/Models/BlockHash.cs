using CYPCore.Network;

namespace CYPCore.Models
{
    public class BlockHash
    {
        public byte[] Hash;
        public ulong Height;
    }

    public class BlockHashPeer
    {
        public Peer Peer;
        public BlockHash BlockHash;
    }
}