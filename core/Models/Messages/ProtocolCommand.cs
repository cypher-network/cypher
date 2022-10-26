// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Models.Messages;

public enum ProtocolCommand : byte
{
    NotFound = 0x00,
    Version = 0x01,
    GetPeer = 0x10,
    GetPeers = 0x11,
    GetBlocks = 0x14,
    SaveBlock = 0x15,
    GetBlockHeight = 0x17,
    GetBlockCount = 0x18,
    GetMemTransaction = 0x19,
    GetTransaction = 0x20,
    Transaction = 0x21,
    BlockGraph = 0x22,
    GetSafeguardBlocks = 0x23,
    GetPosTransaction = 0x24,
    GetTransactionBlockIndex = 0x25,
    Stake = 0x26,
    StakeEnabled = 0x27
}