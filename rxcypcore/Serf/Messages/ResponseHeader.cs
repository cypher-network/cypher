using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject(true)]
    public class ResponseHeader
    {
        public ulong Seq;
        public string Error;
    }
}