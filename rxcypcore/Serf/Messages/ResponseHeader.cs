using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject]
    public class ResponseHeader
    {
        [Key("Seq")]
        public ulong Seq;

        [Key("Error")]
        public string Error;
    }
}