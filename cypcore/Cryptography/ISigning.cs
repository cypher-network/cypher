// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

using CYPCore.Models;

namespace CYPCore.Cryptography
{
    public interface ISigning
    {
        string DefaultSigningKeyName { get; }
        Task<KeyPair> GetOrUpsertKeyName(string keyName);
        Task<byte[]> GePublicKey(string keyName);
        Task<byte[]> Sign(string keyName, byte[] message);
        bool VerifySignature(byte[] signature, byte[] message);
        bool VerifySignature(byte[] signature, byte[] publicKey, byte[] message);
        byte[] CalculateVrfSignature(libsignal.ecc.ECPrivateKey privateKey, byte[] message);
        byte[] VerifyVrfSignature(libsignal.ecc.ECPublicKey publicKey, byte[] message, byte[] signature);
    }
}