using System;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;


namespace CypherNetwork.Models;

public struct PeerCooldown : IComparable<PeerCooldown>
{
    public byte[] IpAddress { get; set; }
    public byte[] PublicKey { get; set; }
    public long Timestamp { get; }

    public PeerCooldown()
    {
        IpAddress = null;
        PublicKey = null;
        Timestamp = Util.GetAdjustedTimeAsUnixTimestamp();
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
