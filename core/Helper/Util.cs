// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Blake3;
using CypherNetwork.Extensions;
using Microsoft.IO;

namespace CypherNetwork.Helper;

/// <summary>
/// </summary>
public static class Util
{
    private const string HexUpper = "0123456789ABCDEF";
    public static readonly RecyclableMemoryStreamManager Manager = new();

    /// <summary>
    /// </summary>
    private static readonly DateTimeOffset UnixRef = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static byte[] GetZeroBytes()
    {
        var bytes = Array.Empty<byte>();
        if ((bytes[^1] & 0x80) != 0) Array.Resize(ref bytes, bytes.Length + 1);

        return bytes;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static string GetAssemblyVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.ToString()}" : string.Empty;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static string EntryAssemblyPath()
    {
        return Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public static OSPlatform GetOperatingSystemPlatform()
    {
        foreach (var platform in new[] { OSPlatform.Linux, OSPlatform.FreeBSD, OSPlatform.OSX, OSPlatform.Windows })
            if (RuntimeInformation.IsOSPlatform(platform))
                return platform;

        throw new NotSupportedException();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static byte[] RandomSocketIdentity()
    {
        return Hasher.Hash(Guid.NewGuid().ToByteArray()).HexToByte();
    }

    /// <summary>
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
    /// </summary>
    /// <param name="a"></param>
    /// <param name="n"></param>
    /// <returns></returns>
    public static BigInteger Mod(BigInteger a, BigInteger n)
    {
        var result = a % n;
        if (result < 0 && n > 0 || result > 0 && n < 0) result += n;

        return result;
    }

    /// <summary>
    /// </summary>
    /// <param name="arrays"></param>
    /// <returns></returns>
    public static byte[] Combine(params byte[][] arrays)
    {
        var ret = new byte[arrays.Sum(x => x.Length)];
        var offset = 0;
        foreach (var data in arrays)
        {
            Buffer.BlockCopy(data, 0, ret, offset, data.Length);
            offset += data.Length;
        }

        return ret;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="arrays"></param>
    /// <returns></returns>
    public static byte[] Combine(IReadOnlyList<byte[]> arrays)
    {
        var ret = new byte[arrays.Sum(x => x.Length)];
        var offset = 0;
        foreach (var data in arrays)
        {
            Buffer.BlockCopy(data, 0, ret, offset, data.Length);
            offset += data.Length;
        }

        return ret;
    }

    // /// <summary>
    // /// </summary>
    // /// <param name="hash"></param>
    // /// <param name="xBase"></param>
    // /// <returns></returns>
    // /// <exception cref="ArgumentNullException"></exception>
    // public static ulong ToHashIdentifier(string hash, int xBase = 5)
    // {
    //     if (hash == null) throw new ArgumentNullException(nameof(hash));
    //     var v = new StringBuilder();
    //     for (var i = 6; i < 12; i++)
    //     {
    //         var c = hash[i];
    //         v.Append(new[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
    //     }
    //
    //     var byteHex = Hasher.Hash(v.ToString().ToBytes());
    //     var id = (ulong)BitConverter.ToInt64(byteHex.HexToByte(), 0);
    //     id = (ulong)Convert.ToInt64(id.ToString()[..xBase]);
    //
    //     return id;
    // }

    /// <summary>
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
                if (localIPs.Contains(hostIp)) return true;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    /// <summary>
    /// </summary>
    /// <param name="uriString"></param>
    /// <returns></returns>
    public static IPEndPoint TryParseAddress(string uriString)
    {
        IPEndPoint endPoint = null;
        if (Uri.TryCreate($"http://{uriString}", UriKind.Absolute, out var url) &&
            IPAddress.TryParse(url.Host, out var ip))
            endPoint = new IPEndPoint(ip, url.Port);

        return endPoint;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public static IPAddress GetIpAddress()
    {
        var host1 = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host1.AddressList)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
            if (!ip.IsPrivate())
            {
                return ip;
            }
        }
        
        return IPAddress.Loopback;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="hostNameOrAddress"></param>
    /// <param name="port"></param>
    /// <returns></returns>
    public static IPEndPoint GetIpEndpointFromHostPort(string hostNameOrAddress, int port)
    {
        if (IPAddress.TryParse(hostNameOrAddress, out IPAddress ipAddress))
            return new IPEndPoint(ipAddress, port);
        IPHostEntry entry;
        try
        {
            entry = Dns.GetHostEntry(hostNameOrAddress);
        }
        catch (SocketException)
        {
            return null;
        }
        ipAddress = entry.AddressList.FirstOrDefault(p => p.AddressFamily == AddressFamily.InterNetwork || p.IsIPv6Teredo);
        return ipAddress == null ? null : new IPEndPoint(ipAddress, port);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="hostAndPort"></param>
    /// <returns></returns>
    public static IPEndPoint GetIpEndPoint(string hostAndPort)
    {
        if (string.IsNullOrEmpty(hostAndPort)) return null;

        try
        {
            var p = hostAndPort.Split(':');
            return GetIpEndpointFromHostPort(p[0], int.Parse(p[1]));
        }
        catch
        {
            // ignored
        }

        return null;
    }
    
    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static DateTime GetUtcNow()
    {
        return DateTime.UtcNow;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private static DateTime GetAdjustedTime()
    {
        return GetUtcNow().Add(TimeSpan.Zero);
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public static long GetAdjustedTimeAsUnixTimestamp()
    {
        return new DateTimeOffset(GetAdjustedTime()).ToUnixTimeSeconds();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="timeStampMask"></param>
    /// <returns></returns>
    public static long GetAdjustedTimeAsUnixTimestamp(uint timeStampMask)
    {
        return GetAdjustedTimeAsUnixTimestamp() & ~timeStampMask;
    }

    /// <summary>
    /// </summary>
    /// <param name="dt"></param>
    /// <returns></returns>
    public static uint DateTimeToUnixTime(DateTimeOffset dt)
    {
        return (uint)DateTimeToUnixTimeLong(dt);
    }

    /// <summary>
    /// </summary>
    /// <param name="dt"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private static ulong DateTimeToUnixTimeLong(DateTimeOffset dt)
    {
        dt = dt.ToUniversalTime();
        if (dt < UnixRef)
            throw new ArgumentOutOfRangeException(nameof(dt));
        var result = (dt - UnixRef).TotalSeconds;
        if (result > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dt));
        return (ulong)result;
    }

    /// <summary>
    /// </summary>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public static DateTimeOffset UnixTimeToDateTime(long timestamp)
    {
        var span = TimeSpan.FromSeconds(timestamp);
        return UnixRef + span;
    }

    /// <summary>
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    /// <exception cref="SocketException"></exception>
    public static void ThrowPortNotFree(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            Task.Delay(100);
            var localEp = new IPEndPoint(IPAddress.Any, port);
            socket.Bind(localEp);
        }
        catch (SocketException ex)
        {
            Throw(new SocketException(ex.ErrorCode));
        }
        finally
        {
            socket.Close();
        }
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    /// <exception cref="SocketException"></exception>
    public static int GetAnyFreePort()
    {
        int port = 0;
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var localEp = new IPEndPoint(IPAddress.Any, 0);
            Task.Delay(100);
            socket.Bind(localEp);
            port = localEp.Port;
        }
        catch (SocketException ex)
        {
            Throw(new SocketException(ex.ErrorCode));
        }
        finally
        {
            socket.Close();
        }

        return port;
    }

    /// <summary>
    /// </summary>
    /// <param name="source"></param>
    /// <param name="destination"></param>
    private static void DeepCopy(byte[] source, byte[] destination)
    {
        if (source.Length != destination.Length)
            throw new ArgumentOutOfRangeException($"{nameof(source)} Bad input arrays in deep copy");
        Array.Copy(source, 0, destination, 0, destination.Length);
    }

    /// <summary>
    /// </summary>
    public static class ConfigurationFile
    {
        private const string AppSettingsFilename = "appsettings.json";

        public static string Local()
        {
            return Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, AppSettingsFilename);
        }

        private static string SystemDefaultLinux()
        {
            return Path.Combine("/etc", "cypher-network", "cypher", AppSettingsFilename);
        }

        private static string SystemDefaultMacOs()
        {
            throw new Exception("No macOS system default implemented yet");
        }

        private static string SystemDefaultWindows()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppSettingsFilename);
        }

        public static string SystemDefault()
        {
            var platform = GetOperatingSystemPlatform();
            if (platform == OSPlatform.Linux) return SystemDefaultLinux();

            if (platform == OSPlatform.OSX) return SystemDefaultMacOs();

            if (platform == OSPlatform.Windows) return SystemDefaultWindows();

            throw new Exception("Unsupported operating system");
        }
    }

    public static byte[] Hash(byte[] hashBytes)
    {
        return hashBytes.Length == 0 ? null : Hasher.Hash(hashBytes).HexToByte();
    }

    public static void Throw(Exception e)
    {
        throw e;
    }
}