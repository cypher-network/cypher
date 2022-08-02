// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using MessagePack;

namespace CypherNetwork.Models;

[MessagePackObject]
public record DataProtectionKey
{
    [Key(0)] public string FriendlyName { get; set; }
    [Key(1)] public string XmlData { get; set; }
}