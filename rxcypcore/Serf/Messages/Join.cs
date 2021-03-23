using MessagePack;

namespace rxcypcore.Serf.Messages
{
    [MessagePackObject]
    public class JoinRequest
    {
        [Key("Existing")] public string[] Existing { get; set; }

        [Key("Replay")] public bool Replay { get; set; }
    }

    [MessagePackObject]
    public class JoinResponse
    {
        [Key("Num")] public uint Peers { get; set; }
    }
}