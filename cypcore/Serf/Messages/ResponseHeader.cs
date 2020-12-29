using MessagePack;

namespace CYPCore.Serf.Message
{

    [MessagePackObject]
    public struct ResponseHeader
    {
        [Key("Seq")]
        public ulong Seq;

        [Key("Error")]
        public string Error;
    }
}
