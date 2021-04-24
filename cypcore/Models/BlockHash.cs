using CYPCore.Network;

namespace CYPCore.Models
{
    public class BlockHash
    {
        public byte[] Hash;
        public long Height;
    }

    public class BlockHashPeer
    {
        public Peer Peer;
        public BlockHash BlockHash;
    }
}