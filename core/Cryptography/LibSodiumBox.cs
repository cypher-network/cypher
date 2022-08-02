using System.Runtime.InteropServices;
using System.Security;

namespace CypherNetwork.Cryptography;

internal static class LibSodiumBox
{
    [SuppressUnmanagedCodeSecurity]
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seal")]
    internal static extern unsafe int Seal(byte* c, byte* m, ulong mlen, byte* pk);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seal_open")]
    internal static extern unsafe int SealOpen(
        byte* m,
        byte* c,
        ulong clen,
        byte* pk,
        byte* sk);
    
    [SuppressUnmanagedCodeSecurity]
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_sealbytes")]
    internal static extern ulong Sealbytes();
}