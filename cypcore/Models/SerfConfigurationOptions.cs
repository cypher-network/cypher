// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Models
{
    public class SerfConfigurationOptions
    {
        public string Advertise { get; set; }
        public string Listening { get; set; }
        public string RPC { get; set; }
        public string Encrypt { get; set; }
        public string SnapshotPath { get; set; }
        public string NodeName { get; set; }
        public int RetryMax { get; set; }
        public bool Rejoin { get; set; }
        public string BroadcastTimeout { get; set; }
        public string Loglevel { get; set; }
        public bool Replay { get; set; }
        public string KeyringFile { get; set; }
    }

    public class SerfRxConfigurationOptionCluster
    {
        public string Name { get; set; }
        public SerfRxConfigurationOptionEndpoint[] SeedNodes { get; set; }
    }

    public class SerfRxConfigurationOptionEndpoint
    {
        public string IPAddress { get; set; }
        public ushort Port { get; set; }

    }

    public class SerfRxConfigurationOptions
    {
        public bool Enabled { get; set; }
        public string NodeName { get; set; }
        public SerfRxConfigurationOptionCluster[] Clusters { get; set; }
        public SerfRxConfigurationOptionEndpoint RPC { get; set; }
    }
}
