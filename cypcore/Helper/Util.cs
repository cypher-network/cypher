// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using System.Numerics;
using System.Security.Cryptography;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using ProtoBuf;

using CYPCore.Extentions;
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

        public static T DeserializeJsonFromStream<T>(Stream stream)
        {
            if (stream == null || stream.CanRead == false)
                return default;

            using var sr = new StreamReader(stream);
            using var jtr = new JsonTextReader(sr);
            var js = new JsonSerializer();
            var searchResult = js.Deserialize<T>(jtr);
            return searchResult;
        }

        public static async Task<string> StreamToStringAsync(Stream stream)
        {
            string content = null;

            if (stream != null)
                using (var sr = new StreamReader(stream))
                    content = await sr.ReadToEndAsync();

            return content;
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

        public static BigInteger ConvertHashToNumber(byte[] hash, BigInteger prime, int bytes)
        {
            var intH = new BigInteger(hash);
            var subString = BigInteger.Parse(intH.ToString().Substring(0, bytes));
            var result = Mod(subString, prime);

            return result;
        }

        public static long GetInt64HashCode(byte[] hash)
        {
            //32Byte hashText separate
            //hashCodeStart = 0~7  8Byte
            //hashCodeMedium = 8~23  8Byte
            //hashCodeEnd = 24~31  8Byte
            //and Fold
            var hashCodeStart = BitConverter.ToInt64(hash, 0);
            var hashCodeMedium = BitConverter.ToInt64(hash, 8);
            var hashCodeEnd = BitConverter.ToInt64(hash, 24);

            long hashCode = hashCodeStart ^ hashCodeMedium ^ hashCodeEnd;

            return hashCode;
        }

        public static byte[] SerializeProto<T>(T data)
        {
            byte[] mArray = default;

            try
            {
                using var ms = new MemoryStream();

                Serializer.Serialize(ms, data);
                mArray = ms.ToArray();
            }
            catch { }
            
            return mArray;
        }

        public static T DeserializeProto<T>(byte[] data)
        {
            T item = default;

            try
            {
                using var ms = new MemoryStream(data);
                item = Serializer.Deserialize<T>(ms);
            }
            catch { }

            return item;
        }

        public static IEnumerable<T> DeserializeListProto<T>(byte[] data) where T : class
        {
            List<T> list = new();
            T item;

            try
            {
                using var ms = new MemoryStream(data);

                while ((item = Serializer.DeserializeWithLengthPrefix<T>(ms, PrefixStyle.Base128, fieldNumber: 1)) != null)
                {
                    list.Add(item);
                }
            }
            catch { }

            return list.AsEnumerable();
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

                var byteHex = SHA384ManagedHash(v.ToString().ToBytes());

                id = (ulong)BitConverter.ToInt64(byteHex, 0);
                id = (ulong)Convert.ToInt64(id.ToString().Substring(0, xBase));
            }
            catch (Exception)
            {
                throw;
            }

            return id;
        }

        public static byte[] SHA384ManagedHash(byte[] data)
        {
            SHA384 sHA384 = new SHA384Managed();
            return sHA384.ComputeHash(data);
        }

        public static byte[] SHA256ManagedHash(byte[] data)
        {
            SHA256 sHA256 = new SHA256Managed();
            return sHA256.ComputeHash(data);
        }

        public static JToken ReadJToken(HttpResponseMessage httpResponseMessage, string attr)
        {
            var read = httpResponseMessage.Content.ReadAsStringAsync().Result;
            var jObject = JObject.Parse(read);
            var jToken = jObject.GetValue(attr);

            return jToken;
        }

        public static void Shuffle<T>(T[] array)
        {
            int n = array.Length;
            for (int i = 0; i < n; i++)
            {
                int r = i + Random.Next(n - i);
                T t = array[r];
                array[r] = array[i];
                array[i] = t;
            }
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

        public static double ShannonEntropy(string input)
        {
            static double logtwo(double num)
            {
                return Math.Log(num) / Math.Log(2);
            }

            static double Contain(string x, char k)
            {
                double count = 0;
                foreach (char Y in x)
                {
                    if (Y.Equals(k))
                        count++;
                }
                return count;
            }

            double infoC = 0;
            double freq;
            string k = "";
            foreach (char c1 in input)
            {
                if (!k.Contains(c1.ToString()))
                    k += c1;
            }
            foreach (char c in k)
            {
                freq = Contain(input, c) / input.Length;
                infoC += freq * logtwo(freq);
            }
            infoC /= -1;

            return infoC;
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
            catch { }
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

            private static string SystemDefaultLinux() => Path.Combine("/etc", "tangram", "cypher", AppSettingsFilename);
            private static string SystemDefaultMacOS() => throw new Exception("No macOS system default implemented yet");
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
