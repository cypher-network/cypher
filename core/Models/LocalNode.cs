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
    public byte[] HttpEndPoint { get; init; }
    public byte[] Listening { get; init; }
    public byte[] Advertise { get; init; }
    public byte[] Version { get; init; }
}