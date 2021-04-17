using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject(true)]
    public class JoinRequest
    {
        public string[] Existing { get; set; }
        public bool Replay { get; set; }
    }
}