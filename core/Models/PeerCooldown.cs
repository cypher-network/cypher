using System;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Network;


namespace CypherNetwork.Models;

public struct PeerCooldown : IComparable<PeerCooldown>
{
    public byte[] IpAddress { get; set; }
    public byte[] PublicKey { get; set; }
    public ulong ClientId { get; set; }
    public long Timestamp { get; }
    public PeerState PeerState { get; set; }

    public PeerCooldown()
    {
        ClientId = 0;
        IpAddress = null;
        PublicKey = null;
        Timestamp = Util.GetAdjustedTimeAsUnixTimestamp();
        PeerState = PeerState.Ready;
    }

    /// <summary>
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(PeerCooldown other)
    {
        if (Equals(this, other)) return 0;
        if (Equals(null, other)) return 1;
        return IpAddress.Xor(other.IpAddress) ? 0 : 1;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(IpAddress, PublicKey);
    }
}
