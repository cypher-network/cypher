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
        public string Profile { get; set; }
        public bool Replay { get; set; }
        public string KeyringFile { get; set; }
        public bool Disabled { get; set; }
    }
}
