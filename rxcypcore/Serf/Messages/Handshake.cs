using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject]
    public class Handshake
    {
        [Key("Version")]
        public int Version { get; set; }
    }

    [MessagePackObject]
    public class Authentication
    {
        [Key("AuthKey")]
        public string AuthenticationKey { get; set; }
    }
}