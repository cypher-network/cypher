// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using Dawn;

namespace CYPCore.Models
{
    public class KeyPair : IDisposable
    {
        public byte[] PrivateKey { get; }
        public byte[] PublicKey { get; }

        public KeyPair(byte[] privateKey, byte[] publicKey)
        {
            Guard.Argument(privateKey, nameof(privateKey)).NotNull().NotEmpty();
            Guard.Argument(publicKey, nameof(publicKey)).NotNull().NotEmpty();
            if (privateKey.Length % 16 != 0)
                throw new ArgumentOutOfRangeException(
                    $"{nameof(privateKey)} Private Key length must be a multiple of 16 bytes.");
            PrivateKey = privateKey;
            PublicKey = publicKey;
        }

        public void Dispose()
        {
            Array.Clear(PrivateKey, 0, 32);
        }
    }
}
