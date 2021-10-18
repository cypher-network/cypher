// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using MessagePack;

namespace CYPCore.Wallet.Models
{
    [MessagePackObject]
    public record KeySet
    {
        [Key(0)] public string ChainCode { get; init; }
        [Key(1)] public string KeyPath { get; init; }
        [Key(2)] public string RootKey { get; init; }
        [Key(3)] public string StealthAddress { get; set; }
    }
}