// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Security;
using System.Numerics;
using CYPCore.Extensions;
using System.Runtime.InteropServices;
using Blake3;

namespace CYPCore.Helper
{
    /// <summary>
    /// 
    /// </summary>
    public static class Util
    {
        private const string HexUpper = "0123456789ABCDEF";

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static byte[] GetZeroBytes()
        {
            byte[] bytes = Array.Empty<byte>();
            if ((bytes[^1] & 0x80) != 0)
            {
                Array.Resize(ref bytes, bytes.Length + 1);
            }

            return bytes;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static string GetAssemblyVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static string EntryAssemblyPath()
        {
            return Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public static OSPlatform GetOperatingSystemPlatform()
        {
            foreach (var platform in new[] { OSPlatform.Linux, OSPlatform.FreeBSD, OSPlatform.OSX, OSPlatform.Windows })
            {
                if (RuntimeInformation.IsOSPlatform(platform))
                {
                    return platform;
                }
            }

            throw new NotSupportedException();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static byte[] RandomDealerIdentity()
        {
            return Hasher.Hash(Guid.NewGuid().ToByteArray()).HexToByte();
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="delimiter"></param>
        /// <returns></returns>
        public static string Pop(string value, string delimiter)
        {
            var stack = new Stack<string>(value.Split(new[] { delimiter }, StringSplitOptions.None));
            return stack.Pop();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        /// <exception cref="NullReferenceException"></exception>
        public static byte[] ToArray(this SecureString s)
        {
            if (s == null) throw new NullReferenceException();
            if (s.Length == 0) return Array.Empty<byte>();
            var result = new List<byte>();
            var ptr = SecureStringMarshal.SecureStringToGlobalAllocAnsi(s);
            try
            {
                var i = 0;
                do
                {
                    var b = Marshal.ReadByte(ptr, i++);
                    if (b == 0) break;
                    result.Add(b);
                } while (true);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocAnsi(ptr);
            }

            return result.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="a"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        public static BigInteger Mod(BigInteger a, BigInteger n)
        {
            var result = a % n;
            if (result < 0 && n > 0 || result > 0 && n < 0)
            {
                result += n;
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="arrays"></param>
        /// <returns></returns>
        public static byte[] Combine(params byte[][] arrays)
        {
            byte[] ret = new byte[arrays.Sum(x => x.Length)];
            int offset = 0;
            foreach (byte[] data in arrays)
            {
                Buffer.BlockCopy(data, 0, ret, offset, data.Length);
                offset += data.Length;
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="xBase"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static ulong ToHashIdentifier(string hash, int xBase = 5)
        {
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            var v = new StringBuilder();
            ulong id;
            try
            {
                for (int i = 6; i < 12; i++)
                {
                    var c = hash[i];
                    v.Append(new char[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
                }

                var byteHex = Hasher.Hash(v.ToString().ToBytes());
                id = (ulong)BitConverter.ToInt64(byteHex.HexToByte(), 0);
                id = (ulong)Convert.ToInt64(id.ToString().Substring(0, xBase));
            }
            catch (Exception)
            {
                throw;
            }

            return id;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        public static bool IsLocalIpAddress(string host)
        {
            try
            {
                var hostIPs = Dns.GetHostAddresses(host);
                var localIPs = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (var hostIp in hostIPs)
                {
                    if (IPAddress.IsLoopback(hostIp)) return true;
                    if (localIPs.Contains(hostIp))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="uriString"></param>
        /// <returns></returns>
        public static IPEndPoint TryParseAddress(string uriString)
        {
            IPEndPoint endPoint = null;
            if (Uri.TryCreate($"http://{uriString}", UriKind.Absolute, out Uri url) &&
                IPAddress.TryParse(url.Host, out IPAddress ip))
            {
                endPoint = new IPEndPoint(ip, url.Port);
            }

            return endPoint;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static DateTime GetUtcNow()
        {
            return DateTime.UtcNow;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static DateTime GetAdjustedTime()
        {
            return GetUtcNow().Add(TimeSpan.Zero);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static long GetAdjustedTimeAsUnixTimestamp()
        {
            return new DateTimeOffset(GetAdjustedTime()).ToUnixTimeSeconds();
        }

        /// <summary>
        /// 
        /// </summary>
        public static class ConfigurationFile
        {
            private const string AppSettingsFilename = "appsettings.json";

            public static string Local() => Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, AppSettingsFilename);

            private static string SystemDefaultLinux() =>
                Path.Combine("/etc", "tangram", "cypher", AppSettingsFilename);

            private static string SystemDefaultMacOS() =>
                throw new Exception("No macOS system default implemented yet");

            private static string SystemDefaultWindows() => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppSettingsFilename);

            public static string SystemDefault()
            {
                var platform = GetOperatingSystemPlatform();
                if (platform == OSPlatform.Linux)
                {
                    return SystemDefaultLinux();
                }

                if (platform == OSPlatform.OSX)
                {
                    return SystemDefaultMacOS();
                }

                if (platform == OSPlatform.Windows)
                {
                    return SystemDefaultWindows();
                }

                throw new Exception("Unsupported operating system");
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        private static readonly DateTimeOffset UnixRef = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static uint DateTimeToUnixTime(DateTimeOffset dt)
        {
            return (uint)DateTimeToUnixTimeLong(dt);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static ulong DateTimeToUnixTimeLong(DateTimeOffset dt)
        {
            dt = dt.ToUniversalTime();
            if (dt < UnixRef)
                throw new ArgumentOutOfRangeException("The supplied datetime can't be expressed in unix timestamp");
            var result = (dt - UnixRef).TotalSeconds;
            if (result > UInt32.MaxValue)
                throw new ArgumentOutOfRangeException("The supplied datetime can't be expressed in unix timestamp");
            return (ulong)result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static DateTimeOffset UnixTimeToDateTime(long timestamp)
        {
            var span = TimeSpan.FromSeconds(timestamp);
            return UnixRef + span;
        }
    }
}