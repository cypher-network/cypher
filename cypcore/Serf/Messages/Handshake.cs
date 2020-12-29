using MessagePack;

namespace CYPCore.Serf.Message
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
