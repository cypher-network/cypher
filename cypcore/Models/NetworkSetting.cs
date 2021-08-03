
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