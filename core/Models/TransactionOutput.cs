// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Models;

[MessagePack.MessagePackObject]
public record TransactionOutput
{
    [MessagePack.Key(0)] public byte[] TxId { get; set; }
    [MessagePack.Key(1)] public int Index { get; set; }
    [MessagePack.Key(2)] public byte[] BlockHash { get; set; }
}