// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Models;

public enum VerifyResult : byte
{
    Succeed = 0x00,
    AlreadyExists = 0x10,
    OutOfMemory = 0x02,
    UnableToVerify = 0x11,
    Invalid = 0x12,
    Unknown = 0x13,
    KeyImageAlreadyExists = 0x24,
    CommitmentNotFound = 0x25,
    SyncRunning = 0x26
}