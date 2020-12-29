using MessagePack;

namespace CYPCore.Serf.Message
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
