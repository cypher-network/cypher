// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

using Isopoh.Cryptography.SecureArray;

namespace CYPCore.Models
{
    public class KeyPair : IDisposable
    {
        public byte[] PrivateKey { get; private set; }
        public byte[] PublicKey { get; }

        public KeyPair(byte[] privateKey, byte[] publicKey)
        {
            if (privateKey.Length % 16 != 0)
                throw new ArgumentOutOfRangeException("Private Key length must be a multiple of 16 bytes.");

            PrivateKey = privateKey;
            PublicKey = publicKey;
        }

        public void Dispose()
        {
            Array.Clear(PrivateKey, 0, 32);
        }
    }

    //public class KeyPair : IDisposable
    //{
    //    private readonly SecureArray<byte> _privateKey;

    //    public KeyPair(byte[] privateKey, byte[] publicKey)
    //    {
    //        if (privateKey.Length % 16 != 0)
    //            throw new ArgumentOutOfRangeException("Private Key length must be a multiple of 16 bytes.");

    //        PublicKey = publicKey;
    //        _privateKey = new SecureArray<byte>(32);

    //        Array.Copy(privateKey, _privateKey.Buffer, privateKey.Length);
    //        Array.Clear(privateKey, 0, 32);
    //    }

    //    ~KeyPair()
    //    {
    //        Dispose();
    //    }

    //    public byte[] PublicKey { get; }

    //    public byte[] PrivateKey => _privateKey.Buffer;

    //    public void Dispose() => _privateKey?.Dispose();
    //}
}
