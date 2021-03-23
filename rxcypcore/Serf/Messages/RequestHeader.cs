using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject]
    public class RequestHeader
    {
        [Key("Command")]
        public string Command { get; set; }

        [Key("Seq")]
        public ulong Sequence { get; set; }
    }
}