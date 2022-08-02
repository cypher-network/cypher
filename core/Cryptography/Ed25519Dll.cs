using System.Runtime.InteropServices;

namespace CypherNetwork.Interop;

/// <summary>
/// ed25519-supercop
/// </summary>
public static class Ed25519Dll
{
    [DllImport("ed25519", EntryPoint = "ed25519_sign")]
    public static extern void Sign([MarshalAs(UnmanagedType.LPArray)] byte[] signature,
        [MarshalAs(UnmanagedType.LPArray)] byte[] message,
        [MarshalAs(UnmanagedType.U4)] int message_len,
        [MarshalAs(UnmanagedType.LPArray)] byte[] public_key,
        [MarshalAs(UnmanagedType.LPArray)] byte[] private_key);

    [DllImport("ed25519", EntryPoint = "ed25519_verify")]
    public static extern bool Verify([MarshalAs(UnmanagedType.LPArray)] byte[] signature,
        [MarshalAs(UnmanagedType.LPArray)] byte[] message,
        [MarshalAs(UnmanagedType.U4)] int message_len,
        [MarshalAs(UnmanagedType.LPArray)] byte[] public_key);
}