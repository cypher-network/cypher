// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;

namespace CypherNetwork.Models;

public class Config
{
    public Node Node { get; set; }
}

public record Node
{
    public const string Mainnet = "mainnet";
    public const string Testnet = "testnet";
    public string Name { get; set; }
    [JsonIgnore]
    public IPEndPoint EndPoint { get; set; }
    public Network Network { get; set; }
    public Data Data { get; set; }
    public Staking Staking { get; set; }
}

public record Network
{
    public string Environment { get; set; }
    public int AutoSyncEveryMinutes { get; set; }
    public LettuceEncrypt LettuceEncrypt { get; set; }
    public X509Certificate X509Certificate { get; set; }
    public TransactionLeakRateConfigurationOption TransactionRateConfig { get; set; }
    public string SigningKeyRingName { get; set; }
    public int HttpPort { get; set; }
    public int HttpsPort { get; set; }
    public P2P P2P { get; set; }
    public IList<string> SeedList { get; set; }
    public IList<string> SeedListPublicKeys { get; set; }
    public string CertificateMode { get; set; }
}

public record P2P
{
    public int TcpPort { get; set; }
    public int WsPort { get; set; }
}

public record LettuceEncrypt
{
    public bool AcceptTermsOfService { get; set; }
    public string[] DomainNames { get; set; }
    public string EmailAddress { get; set; }
}

public record X509Certificate
{
    public string CertPath { get; set; }
    public string Password { get; set; }
    public string Thumbprint { get; set; }
}

public record Data
{
    public string RocksDb { get; set; }
    public string KeysProtectionPath { get; set; }
}

public record Staking
{
    public bool Enabled { get; set; }
    public int MaxTransactionsPerBlock { get; set; }
    public int MaxTransactionSizePerBlock { get; set; }
    public string RewardAddress { get; set; }
    
}