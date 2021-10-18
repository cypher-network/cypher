namespace CYPCore.Ledger
{
    public enum VerifyResult : byte
    {
        Succeed = 0x00,
        AlreadyExists = 0x10,
        OutOfMemory = 0x02,
        UnableToVerify = 0x11,
        Invalid = 0x12,
        Unknown = 0x13,
        KeyImageAlreadyExists = 0x24,
        CommitmentNotFound = 0x25
    }
}