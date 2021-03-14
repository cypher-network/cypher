namespace CYPCore.Ledger
{
    public enum VerifyResult : sbyte
    {
        Succeed,
        AlreadyExists,
        OutOfMemory,
        UnableToVerify,
        Invalid,
        Unknown
    }
}