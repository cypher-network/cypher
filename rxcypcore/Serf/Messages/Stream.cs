using System.Collections.Generic;
using MessagePack;

namespace rxcypcore.Serf.Messages
{
    public class Stream
    {
        [MessagePackObject]
        public class StreamRequest
        {
            [Key("Type")] public string Type { get; set; }
        }
    }
}