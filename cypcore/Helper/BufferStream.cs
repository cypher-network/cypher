// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Text;

namespace CYPCore.Helper
{
    /// <summary>
    /// 
    /// </summary>
    public class BufferStream : IDisposable
    {
        private byte[] _buffer = Array.Empty<byte>();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public int Size()
        {
            return _buffer.Length;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public BufferStream Append(byte[] bytes)
        {
            int i = _buffer.Length;
            Array.Resize(ref _buffer, i + bytes.Length + 4);

            byte[] lengthBytes = BitConverter.GetBytes(bytes.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            lengthBytes.CopyTo(_buffer, i);
            bytes.CopyTo(_buffer, i + 4);

            return this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public BufferStream Append(string value)
        {
            return Append(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public BufferStream Append(string[] value)
        {
            byte[] bytes = Array.ConvertAll(value, byte.Parse);
            return Append(bytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public BufferStream Append(int value)
        {
            byte[] lengthBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            return Append(lengthBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public BufferStream Append(uint value)
        {
            byte[] lengthBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            return Append(lengthBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public BufferStream Append(long value)
        {
            byte[] lengthBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            return Append(lengthBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public BufferStream Append(ushort value)
        {
            byte[] lengthBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            return Append(lengthBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public BufferStream Append(ulong value)
        {
            byte[] lengthBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            return Append(lengthBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            byte[] result = new byte[4 + _buffer.Length];

            byte[] lengthBytes = BitConverter.GetBytes(_buffer.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            lengthBytes.CopyTo(result, 0);
            _buffer.CopyTo(result, 4);

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
        }
    }
}
