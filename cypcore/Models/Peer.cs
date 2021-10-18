// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class Peer
    {
        [Key(0)] public string RestApi { get; set; }
        [Key(1)] public ulong BlockHeight { get; set; }
        [Key(2)] public ulong ClientId { get; set; }
        [Key(3)] public string Listening { get; set; }
        [Key(4)] public string Name { get; set; }
        [Key(5)] public string PublicKey { get; set; }
        [Key(6)] public string Version { get; set; }

    }
}
