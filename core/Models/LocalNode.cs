// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

namespace CypherNetwork.Models;

/// <summary>
/// 
/// </summary>
[Serializable]
public record LocalNode
{
    public ulong Identifier { get; init; }
    public byte[] PublicKey { get; init; }
    public byte[] Name { get; init; }
    public byte[] TcpPort { get; init; }
    public byte[] WsPort { get; init; }
    public byte[] HttpPort { get; init; }
    public byte[] HttpsPort { get; init; }
    public byte[] IpAddress { get; init; }
    public byte[] Version { get; init; }
}