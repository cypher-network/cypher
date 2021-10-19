using System;
using System.IO;
using System.Net;

namespace CYPCore.GossipMesh
{
    /// <summary>
    /// 
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static MessageType ReadMessageType(this Stream stream)
        {
            return (MessageType)stream.ReadByte();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static MemberState ReadMemberState(this Stream stream)
        {
            return (MemberState)stream.ReadByte();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static IPAddress ReadIPAddress(this Stream stream)
        {
            return new IPAddress(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte(), (byte)stream.ReadByte() });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static ushort ReadPort(this Stream stream)
        {
            var bigByte = (byte)stream.ReadByte();
            var littleByte = (byte)stream.ReadByte();

            return BitConverter.IsLittleEndian ?
             BitConverter.ToUInt16(new byte[] { littleByte, bigByte }, 0) :
             BitConverter.ToUInt16(new byte[] { bigByte, littleByte }, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static IPEndPoint ReadIPEndPoint(this Stream stream)
        {
            return new IPEndPoint(stream.ReadIPAddress(), stream.ReadPort());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="ipAddress"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteIPAddress(this Stream stream, IPAddress ipAddress)
        {
            if (ipAddress == null)
            {
                throw new ArgumentNullException(nameof(ipAddress));
            }

            stream.Write(ipAddress.GetAddressBytes(), 0, 4);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="port"></param>
        public static void WritePort(this Stream stream, ushort port)
        {
            stream.WriteByte((byte)(port >> 8));
            stream.WriteByte((byte)port);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="ipEndPoint"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteIPEndPoint(this Stream stream, IPEndPoint ipEndPoint)
        {
            if (ipEndPoint == null)
            {
                throw new ArgumentNullException(nameof(ipEndPoint));
            }

            stream.WriteIPAddress(ipEndPoint.Address);
            stream.WritePort((ushort)ipEndPoint.Port);
        }
    }
}