// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

namespace CypherNetwork.Extensions;

public static class StringExtensions
{
    public static SecureString ToSecureString(this string value)
    {
        var secureString = new SecureString();
        Array.ForEach(value.ToArray(), secureString.AppendChar);
        secureString.MakeReadOnly();
        return secureString;
    }

    public static byte[] HexToByte(this string hex)
    {
        return Convert.FromHexString(hex);
    }

    public static byte[] HexToByte<T>(this T hex)
    {
        return Convert.FromHexString(hex.ToString()!);
    }

    public static void ZeroString(this string value)
    {
        var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
        unsafe
        {
            var pValue = (char*)handle.AddrOfPinnedObject();
            for (var index = 0; index < value.Length; index++) pValue![index] = char.MinValue;
        }

        handle.Free();
    }
}