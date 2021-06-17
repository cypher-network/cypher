using System;
using System.Text;

namespace CYPCore.Helper
{
    public class TangramStream : IDisposable
    {
        private byte[] buffer = Array.Empty<byte>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public TangramStream Append(byte[] bytes)
        {
            int i = buffer.Length;
            Array.Resize(ref buffer, i + bytes.Length + 4);

            byte[] lengthBytes = BitConverter.GetBytes(bytes.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            lengthBytes.CopyTo(buffer, i);
            bytes.CopyTo(buffer, i + 4);

            return this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public TangramStream Append(string value)
        {
            return Append(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public TangramStream Append(string[] value)
        {
            byte[] bytes = Array.ConvertAll(value, byte.Parse);
            return Append(bytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public TangramStream Append(int value)
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
        public TangramStream Append(uint value)
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
        public TangramStream Append(long value)
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
        public TangramStream Append(ushort value)
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
        public TangramStream Append(ulong value)
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
            byte[] result = new byte[4 + buffer.Length];

            byte[] lengthBytes = BitConverter.GetBytes(buffer.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);

            lengthBytes.CopyTo(result, 0);
            buffer.CopyTo(result, 4);

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
    }
}
