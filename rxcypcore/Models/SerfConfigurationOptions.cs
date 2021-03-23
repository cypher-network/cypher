namespace rxcypcore.Models
{
    public class SerfConfigurationOptionCluster
    {
        public string Name { get; set; }
        public SerfConfigurationOptionEndpoint[] SeedNodes { get; set; }
    }

    public class SerfConfigurationOptionEndpoint
    {
        public string IPAddress { get; set; }
        public ushort Port { get; set; }

    }

    public class SerfConfigurationOptions
    {
        public bool Enabled { get; set; }
        public string NodeName { get; set; }
        public SerfConfigurationOptionCluster[] Clusters { get; set; }
        public SerfConfigurationOptionEndpoint RPC { get; set; }
    }
}