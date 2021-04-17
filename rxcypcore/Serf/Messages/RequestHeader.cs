using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject(true)]
    public class RequestHeader
    {
        public string Command { get; set; }
        public ulong Seq { get; set; }
    }
}