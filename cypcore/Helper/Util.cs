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
using System.Security.Cryptography;
using CYPCore.Extensions;
using System.Runtime.InteropServices;

namespace CYPCore.Helper
{
    public static class Util
    {
        private const string HexUpper = "0123456789ABCDEF";

        private static readonly Random Random = new Random();

        public static byte[] GetZeroBytes()
        {
            byte[] bytes = Array.Empty<byte>();
            if ((bytes[^1] & 0x80) != 0)
            {
                Array.Resize(ref bytes, bytes.Length + 1);
            }

            return bytes;
        }

        public static string GetAssemblyVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        public static string EntryAssemblyPath()
        {
            return Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        }

        public static OSPlatform GetOperatingSystemPlatform()
        {
            foreach (var platform in new[]
            {
                OSPlatform.Linux,
                OSPlatform.FreeBSD,
                OSPlatform.OSX,
                OSPlatform.Windows
            })
            {
                if (RuntimeInformation.IsOSPlatform(platform))
                {
                    return platform;
                }
            }

            throw new NotSupportedException();
        }

        public static string Pop(string value, string delimiter)
        {
            var stack = new Stack<string>(value.Split(new string[] { delimiter }, StringSplitOptions.None));
            return stack.Pop();
        }

        public static byte[] ToArray(this SecureString s)
        {
            if (s == null)
                throw new NullReferenceException();
            if (s.Length == 0)
                return Array.Empty<byte>();
            var result = new List<byte>();
            IntPtr ptr = SecureStringMarshal.SecureStringToGlobalAllocAnsi(s);
            try
            {
                int i = 0;
                do
                {
                    byte b = Marshal.ReadByte(ptr, i++);
                    if (b == 0)
                        break;
                    result.Add(b);
                } while (true);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocAnsi(ptr);
            }

            return result.ToArray();
        }

        public static BigInteger Mod(BigInteger a, BigInteger n)
        {
            var result = a % n;
            if ((result < 0 && n > 0) || (result > 0 && n < 0))
            {
                result += n;
            }

            return result;
        }

        public static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }

        public static byte[] Combine(byte[] first, byte[] second, byte[] third)
        {
            byte[] ret = new byte[first.Length + second.Length + third.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            Buffer.BlockCopy(third, 0, ret, second.Length, third.Length);
            return ret;
        }

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

        public static ulong HashToId(string hash, int xBase = 5)
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));

            var v = new StringBuilder();
            ulong id;

            try
            {
                for (int i = 6; i < 12; i++)
                {
                    var c = hash[i];
                    v.Append(new char[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
                }

                var byteHex = Sha384ManagedHash(v.ToString().ToBytes());

                id = (ulong)BitConverter.ToInt64(byteHex, 0);
                id = (ulong)Convert.ToInt64(id.ToString().Substring(0, xBase));
            }
            catch (Exception)
            {
                throw;
            }

            return id;
        }

        public static byte[] Sha384ManagedHash(byte[] data)
        {
            SHA384 sHA384 = new SHA384Managed();
            return sHA384.ComputeHash(data);
        }

        public static byte[] Sha256ManagedHash(byte[] data)
        {
            SHA256 sHA256 = new SHA256Managed();
            return sHA256.ComputeHash(data);
        }

        public static BigInteger Exp(BigInteger a, BigInteger exponent, BigInteger n)
        {
            if (exponent < 0)
                throw new Exception("Cannot raise a BigInteger to a negative power.", null);

            if (n < new BigInteger(2))
                throw new Exception("Cannot perform a modulo operation against a BigInteger less than 2.", null);

            if (BigInteger.Abs(a) >= n)
            {
                a %= n;
            }

            if (a.Sign == 1)
            {
                a += n;
            }

            if (a == new BigInteger())
                return new BigInteger();

            BigInteger res = new BigInteger(1);
            BigInteger factor = new BigInteger(a.ToByteArray());

            while (exponent > new BigInteger())
            {
                if (exponent % new BigInteger(2) == new BigInteger())
                    res = (res * factor) % n;
                exponent /= new BigInteger(2);
                factor = (factor * factor) % n;
            }

            return res;
        }

        public static bool IsLocalIpAddress(string host)
        {
            try
            {
                IPAddress[] hostIPs = Dns.GetHostAddresses(host);
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

                foreach (IPAddress hostIP in hostIPs)
                {
                    if (IPAddress.IsLoopback(hostIP)) return true;
                    foreach (IPAddress localIP in localIPs)
                    {
                        if (hostIP.Equals(localIP)) return true;
                    }
                }
            }
            catch
            {
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

        public static DateTime GetUtcNow()
        {
            return DateTime.UtcNow;
        }

        public static DateTime GetAdjustedTime()
        {
            return GetUtcNow().Add(TimeSpan.Zero);
        }

        public static long GetAdjustedTimeAsUnixTimestamp()
        {
            return new DateTimeOffset(GetAdjustedTime()).ToUnixTimeSeconds();
        }

        public static class ConfigurationFile
        {
            private const string AppSettingsFilename = "appsettings.json";

            public static string Local() => Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                AppSettingsFilename);

            private static string SystemDefaultLinux() =>
                Path.Combine("/etc", "tangram", "cypher", AppSettingsFilename);

            private static string SystemDefaultMacOS() =>
                throw new Exception("No macOS system default implemented yet");

            private static string SystemDefaultWindows() => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppSettingsFilename);

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
    }
}