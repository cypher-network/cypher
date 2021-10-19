// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

namespace CYPCore.Extensions
{
    public static class StringExtensions
    {
        public static SecureString ToSecureString(this string value)
        {
            var secureString = new SecureString();
            Array.ForEach(value.ToArray(), secureString.AppendChar);
            secureString.MakeReadOnly();
            return secureString;
        }

        public static byte[] HexToByte(this string hex) => Hex2Byte(hex);
        public static byte[] HexToByte<T>(this T hex) => Hex2Byte(hex.ToString());

        private static byte[] Hex2Byte(string s)
        {
            byte[] bytes = new byte[s.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = s[i * 2] - 65;
                hi = hi + 10 + ((hi >> 31) & 7);

                int lo = s[i * 2 + 1] - 65;
                lo = lo + 10 + ((lo >> 31) & 7) & 0x0f;

                bytes[i] = (byte)(lo | hi << 4);
            }
            return bytes;
        }

        public static void ZeroString(this string value)
        {
            var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
            unsafe
            {
                var pValue = (char*)handle.AddrOfPinnedObject();
                for (int index = 0; index < value.Length; index++)
                {
                    pValue[index] = char.MinValue;
                }
            }

            handle.Free();
        }
    }
}
