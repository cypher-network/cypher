// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Models;

public record NetworkSetting
{
    public const string Mainnet = "mainnet";
    public const string Testnet = "testnet";

    public string Environment { get; set; }
    public int AutoSyncEveryMinutes { get; set; }
    public X509Certificate X509Certificate { get; set; }
    public TransactionLeakRateConfigurationOption TransactionRateConfig { get; set; }
    public string SigningKeyRingName { get; set; }
}

public record X509Certificate
{
    public string CertPath { get; set; }
    public string Password { get; set; }
    public string Thumbprint { get; set; }
}