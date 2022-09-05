// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using Blake3;

namespace CypherNetwork.Extensions;

public static class ByteExtensions
{
    public static byte[] ToBytes(this string value)
    {
        return Encoding.UTF8.GetBytes(value ?? string.Empty, 0, value!.Length);
    }

    public static ReadOnlySpan<byte> ToBytes(this ReadOnlySpan<char> value)
    {
        Span<byte> bytes = stackalloc byte[value.Length];
        Encoding.UTF8.GetBytes(value, bytes);
        return bytes.ToArray();
    }

    public static byte[] ToBytes(this ulong value)
    {
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    public static byte[] ToBytes(this long value)
    {
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    public static byte[] ToBytes(this bool value)
    {
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    public static byte[] ToBytes(this uint value)
    {
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    public static byte[] ToBytes(this int value)
    {
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    public static byte[] ToBytes(this ushort value)
    {
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    public static string ByteToHex(this byte[] data)
    {
        return Convert.ToHexString(data);
    }

    public static string ByteToHex(this ReadOnlySpan<byte> data)
    {
        return Convert.ToHexString(data);
    }

    public static string FromBytes(this byte[] data)
    {
        return Encoding.UTF8.GetString(data);
    }

    public static IEnumerable<byte[]> Split(this byte[] value, int bufferLength)
    {
        var countOfArray = value.Length / bufferLength;
        if (value.Length % bufferLength > 0)
            countOfArray++;
        for (var i = 0; i < countOfArray; i++) yield return value.Skip(i * bufferLength).Take(bufferLength).ToArray();
    }

    public static bool Xor(this byte[] a, byte[] b)
    {
        if (a == null) return false;
        var x = a.Length ^ b.Length;
        for (var i = 0; i < a.Length && i < b.Length; ++i) x |= a[i] ^ b[i];
        return x == 0;
    }

    public static bool Xor(this Span<byte> a, Span<byte> b)
    {
        if (a.IsEmpty) return false;
        var x = a.Length ^ b.Length;
        for (var i = 0; i < a.Length && i < b.Length; ++i) x |= a[i] ^ b[i];
        return x == 0;
    }

    public static ulong ToHashIdentifier(this Span<byte> hash)
    {
        var byteHex = Hasher.Hash(hash);
        ReadOnlySpan<byte> h = byteHex.AsSpanUnsafe();
        var id = (ulong)BitConverter.ToInt64(h);
        id = (ulong)Convert.ToInt64(id.ToString()[..5]);
        return id;
    }

    public static ulong ToHashIdentifier(this ReadOnlySpan<byte> hash)
    {
        var byteHex = Hasher.Hash(hash);
        ReadOnlySpan<byte> h = byteHex.AsSpanUnsafe();
        var id = (ulong)BitConverter.ToInt64(h);
        id = (ulong)Convert.ToInt64(id.ToString()[..5]);
        return id;
    }

    public static ulong ToHashIdentifier(this byte[] hash)
    {
        var byteHex = Hasher.Hash(hash);
        ReadOnlySpan<byte> h = byteHex.AsSpanUnsafe();
        var id = (ulong)BitConverter.ToInt64(h);
        id = (ulong)Convert.ToInt64(id.ToString()[..5]);
        return id;
    }

    public static byte[] EnsureNotNull(this byte[] source)
    {
        return source ?? Array.Empty<byte>();
    }

    public static byte[] WrapLengthPrefix(this byte[] message)
    {
        var lengthPrefix = BitConverter.GetBytes(message.Length);
        var ret = new byte[lengthPrefix.Length + message.Length];
        lengthPrefix.CopyTo(ret, 0);
        message.CopyTo(ret, lengthPrefix.Length);
        return ret;
    }

    public static byte[] WrapLengthPrefix(this Span<byte> message)
    {
        ReadOnlySpan<byte> lengthPrefix = BitConverter.GetBytes(message.Length);
        Span<byte> ret = stackalloc byte[lengthPrefix.Length + message.Length];
        lengthPrefix.CopyTo(ret);
        message.CopyTo(ret);
        return ret.ToArray();
    }

    public static byte[] WrapLengthPrefix(this ReadOnlySpan<byte> message)
    {
        ReadOnlySpan<byte> lengthPrefix = BitConverter.GetBytes(message.Length);
        Span<byte> ret = stackalloc byte[lengthPrefix.Length + message.Length];
        lengthPrefix.CopyTo(ret);
        message.CopyTo(ret);
        return ret.ToArray();
    }

    [SecurityCritical]
    public static SecureString ToSecureString(this byte[] value)
    {
        unsafe
        {
            fixed (char* chr = Encoding.UTF8.GetString(value).ToCharArray())
            {
                return new SecureString(chr, value.Length);
            }
        }
    }

    public static void Destroy(this byte[] value)
    {
        if (value == null) return;
        for (var i = 0; i < value.Length; i++) value[i] = 0;
    }
}