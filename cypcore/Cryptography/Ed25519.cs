using CYPCore.Interop;

namespace CYPCore.Cryptography
{
    /// <summary>
    /// 
    /// </summary>
    public static class Ed25519
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="public_key"></param>
        /// <param name="private_key"></param>
        /// <returns></returns>
        public static byte[] Sign(byte[] message, byte[] public_key, byte[] private_key)
        {
            var sig = new byte[64];
            Ed25519Dll.Sign(sig, message, message.Length, public_key, private_key);
            return sig;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signature"></param>
        /// <param name="message"></param>
        /// <param name="public_key"></param>
        /// <returns></returns>
        public static bool Verify(byte[] signature, byte[] message, byte[] public_key)
        {
            return Ed25519Dll.Verify(signature, message, message.Length, public_key);
        }
    }
}
