// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Models
{
    public class NetworkSetting
    {
        public const string Mainnet = "mainnet";
        public const string Testnet = "testnet";

        public string Environment { get; set; }
        public TransactionLeakRateConfigurationOption TransactionRateConfig { get; set; }
    }
}