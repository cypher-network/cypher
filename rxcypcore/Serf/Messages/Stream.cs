using MessagePack;

namespace rxcypcore.Serf.Messages
{
    public class Stream
    {
        [MessagePackObject(true)]
        public class StreamRequest
        {
            public string Type { get; set; }
        }
    }
}