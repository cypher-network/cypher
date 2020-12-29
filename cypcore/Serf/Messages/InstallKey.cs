using MessagePack;

using System.Collections.Generic;

namespace CYPCore.Serf.Message
{
    [MessagePackObject]
    public class KeyRequest
    {
        [Key("Key")]
        public string Key { get; set; }
    }

    [MessagePackObject]
    public class KeyActionResponse
    {
        [Key("Messages")]
        public IDictionary<string, string> Messages { get; set; }

        [Key("NumErr")]
        public uint NumberOfErrors { get; set; }

        [Key("NumNodes")]
        public uint NumberOfNodes { get; set; }

        [Key("NumResp")]
        public uint NumberOfResponses { get; set; }
    }

    [MessagePackObject]
    public class KeyListResponse : KeyActionResponse
    {
        [Key("Keys")]
        public Dictionary<string, int> Keys { get; set; }
    }
}
