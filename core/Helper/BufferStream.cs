// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.IO;

namespace CypherNetwork.Helper;

/// <summary>
/// </summary>
public class BufferStream : IDisposable
{
    private byte[] _buffer = Array.Empty<byte>();

    /// <summary>
    /// </summary>
    public void Dispose()
    {
        Clear();
    }

    /// <summary>
    /// 
    /// </summary>
    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _buffer = null;
        _buffer = Array.Empty<byte>();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public int Size()
    {
        return _buffer.Length;
    }

    /// <summary>
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    public BufferStream Append(byte[] bytes)
    {
        var i = _buffer.Length;
        Array.Resize(ref _buffer, i + bytes.Length + 4);

        var lengthBytes = BitConverter.GetBytes(bytes.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        lengthBytes.CopyTo(_buffer, i);
        bytes.CopyTo(_buffer, i + 4);

        return this;
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public BufferStream Append(string value)
    {
        return Append(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public BufferStream Append(string[] value)
    {
        var bytes = Array.ConvertAll(value, byte.Parse);
        return Append(bytes);
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public BufferStream Append(int value)
    {
        var lengthBytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        return Append(lengthBytes);
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public BufferStream Append(uint value)
    {
        var lengthBytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        return Append(lengthBytes);
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public BufferStream Append(long value)
    {
        var lengthBytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        return Append(lengthBytes);
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public BufferStream Append(ushort value)
    {
        var lengthBytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        return Append(lengthBytes);
    }

    /// <summary>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public BufferStream Append(ulong value)
    {
        var lengthBytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        return Append(lengthBytes);
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] ToArray()
    {
        var result = new byte[4 + _buffer.Length];

        var lengthBytes = BitConverter.GetBytes(_buffer.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        lengthBytes.CopyTo(result, 0);
        _buffer.CopyTo(result, 4);

        return result;
    }
}