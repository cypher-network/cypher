// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Blake3;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;

namespace CypherNetwork.Models;

[MessagePack.MessagePackObject]
public record Transaction: IComparable<Transaction>
{
    /// <summary>
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(Transaction other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        var txIdComparison = string.Compare(TxnId.ByteToHex(), other.TxnId.ByteToHex(), StringComparison.Ordinal);
        return txIdComparison != 0
            ? txIdComparison
            : string.Compare(TxnId.ByteToHex(), other.TxnId.ByteToHex(), StringComparison.Ordinal);
    }
    
    [MessagePack.Key(0)] public byte[] TxnId { get; set; }
    [MessagePack.Key(1)] public Bp[] Bp { get; set; }
    [MessagePack.Key(2)] public int Ver { get; set; }
    [MessagePack.Key(3)] public int Mix { get; set; }
    [MessagePack.Key(4)] public Vin[] Vin { get; set; }
    [MessagePack.Key(5)] public Vout[] Vout { get; set; }
    [MessagePack.Key(6)] public Rct[] Rct { get; set; }
    [MessagePack.Key(7)] public Vtime Vtime { get; set; }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ValidationResult> HasErrors()
    {
        var results = new List<ValidationResult>();
        if (TxnId == null) results.Add(new ValidationResult("Argument is null", new[] { "TxnId" }));
        if (TxnId != null && TxnId.Length != 32)
            results.Add(new ValidationResult("Range exception", new[] { "TxnId" }));
        if (!TxnId.Xor(ToHash())) results.Add(new ValidationResult("Range exception", new[] { "TxnId" }));
        if (Mix < 0) results.Add(new ValidationResult("Range exception", new[] { "Mix" }));
        if (Mix != 22) results.Add(new ValidationResult("Range exception", new[] { "Mix" }));
        if (Rct == null) results.Add(new ValidationResult("Argument is null", new[] { "Rct" }));
        if (!((Ver >= ushort.MinValue) & (Ver <= ushort.MaxValue)))
            results.Add(new ValidationResult("Incorrect number", new[] { "Ver" }));
        if (Vin == null) results.Add(new ValidationResult("Argument is null", new[] { "Vin" }));
        if (Vout == null) results.Add(new ValidationResult("Argument is null", new[] { "Vout" }));
        if (Bp != null)
            foreach (var bp in Bp)
                results.AddRange(bp.Validate());
        if (Vin != null)
            foreach (var vi in Vin)
                results.AddRange(vi.Validate());
        if (Vout != null)
            foreach (var vo in Vout)
                results.AddRange(vo.Validate());
        if (Rct != null)
            foreach (var rct in Rct)
                results.AddRange(rct.Validate());
        if (OutputType() != CoinType.Payment) return results;
        if (Vtime == null) results.Add(new ValidationResult("Argument is null", new[] { "Vtime" }));
        if (Vtime != null) results.AddRange(Vtime.Validate());
        return results;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] ToHash()
    {
        return Hasher.Hash(ToStream()).HexToByte();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] ToStream()
    {
        using var ts = new BufferStream();
        ts
            .Append(Mix)
            .Append(Ver);

        foreach (var bp in Bp) ts.Append(bp.Proof);

        foreach (var vin in Vin)
        {
            ts.Append(vin.Image);
            ts.Append(vin.Offsets);
        }

        foreach (var vout in Vout)
        {
            ts
                .Append(vout.A)
                .Append(vout.C)
                .Append(vout.E)
                .Append(vout.L)
                .Append(vout.N)
                .Append(vout.P)
                .Append(vout.S ?? Array.Empty<byte>())
                .Append(Enum.GetName(vout.T))
                .Append(vout.D ?? Array.Empty<byte>());
        }

        foreach (var rct in Rct)
            ts
                .Append(rct.I)
                .Append(rct.M)
                .Append(rct.P)
                .Append(rct.S);

        if (Vtime != null)
            ts
                .Append(Vtime.I)
                .Append(Vtime.L)
                .Append(Vtime.M)
                .Append(Vtime.N)
                .Append(Vtime.S)
                .Append(Vtime.W);

        return ts.ToArray();
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public CoinType OutputType()
    {
        var coinType = CoinType.Empty;
        var outputs = Vout.Select(x => Enum.GetName(x.T)).ToArray();
        if (outputs.Contains(Enum.GetName(CoinType.Payment)) && outputs.Contains(Enum.GetName(CoinType.Change)))
            coinType = CoinType.Payment;
        if (outputs.Contains(Enum.GetName(CoinType.Coinbase)) && outputs.Contains(Enum.GetName(CoinType.Coinstake)))
            coinType = CoinType.Coinstake;
        return coinType;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public ushort GetSize()
    {
        return (ushort)ToStream().Length;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(TxnId.ByteToHex());
    }
}