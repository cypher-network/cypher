using System.Runtime.InteropServices;
using System.Security;

namespace CypherNetwork.Cryptography;

internal static class LibSodiumChacha20Poly1305
{
    [SuppressUnmanagedCodeSecurity]
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_aead_chacha20poly1305_encrypt")]
    internal static extern unsafe int Encrypt(
        byte* c,
        ulong* clen_p,
        byte* m,
        ulong mlen,
        byte* ad,
        ulong adlen,
        byte* nsec,
        byte* npub,
        byte* k);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_aead_chacha20poly1305_decrypt")]
    internal static extern unsafe int Decrypt(
        byte* m,
        ref ulong mlen_p,
        byte* nsec,
        byte* c,
        ulong clen,
        byte* ad,
        ulong adlen,
        byte* npub,
        byte* k);
}