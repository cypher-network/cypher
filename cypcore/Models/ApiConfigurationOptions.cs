using System.Collections.Generic;

namespace CYPCore.Models
{
    public class AppOptions
    {
        public string Name { get; set; }
        public string RestApi { get; set; }
        public GossipOptions Gossip { get; set; }
        public DataOptions Data { get; set; }
        public StakingOptions Staking { get; set; }
        public NetworkSetting Network { get; set; }
    }

    public class GossipOptions
    {
        public string Listening { get; set; }
        public List<string> SeedNodes { get; set; }
        public bool SyncOnlyWithSeedNodes { get; set; }
    }

    public class DataOptions
    {
        public string RocksDb { get; set; }
        public string KeysProtectionPath { get; set; }
    }
}
