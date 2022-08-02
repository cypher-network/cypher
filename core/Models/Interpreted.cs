// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Text;
using Blake3;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using Newtonsoft.Json;

namespace CypherNetwork.Models;

[MessagePack.MessagePackObject]
public record Interpreted
{
    private const string HexUpper = "0123456789ABCDEF";
    [MessagePack.Key(0)] public string Hash { get; set; }
    [MessagePack.Key(1)] public ulong Node { get; set; }
    [MessagePack.Key(2)] public ulong Round { get; set; }
    [MessagePack.Key(3)] public object Data { get; set; }
    [MessagePack.Key(4)] public string PublicKey { get; set; }
    [MessagePack.Key(5)] public string Signature { get; set; }
    [MessagePack.Key(6)] public string PreviousHash { get; set; }

    public override string ToString()
    {
        var v = new StringBuilder();
        v.Append(Node);
        v.Append(" | ");
        v.Append(Round);
        if (string.IsNullOrEmpty(Hash)) return v.ToString();
        v.Append(" | ");
        for (var i = 6; i < 12; i++)
        {
            var c = Hash[i];
            v.Append(new[] { HexUpper[c >> 4], HexUpper[c & 0x0f] });
        }

        return v.ToString();
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
        ts.Append(Hash)
            .Append(Node)
            .Append(PreviousHash ?? string.Empty)
            .Append(Round)
            .Append(PublicKey ?? string.Empty)
            .Append(Signature ?? string.Empty);
        return ts.ToArray();
    }

    /// <summary>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T Cast<T>()
    {
        var json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<T>(json);
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public byte[] ToIdentifier()
    {
        return ToHash().ByteToHex().ToBytes();
    }
}