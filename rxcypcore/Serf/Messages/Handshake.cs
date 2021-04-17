using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject(true)]
    public class Handshake
    {
        public int Version { get; set; }
    }
}