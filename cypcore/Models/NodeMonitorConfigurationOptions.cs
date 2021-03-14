namespace CYPCore.Models
{
    public class NodeMonitorConfigurationOptionsTester
    {
        public string Listening { get; set; }
        public ushort Port { get; set; }
    }

    public class NodeMonitorConfigurationOptions
    {
        public static string ConfigurationSectionName = "NodeMonitor";

        public bool Enabled { get; set; }

        public NodeMonitorConfigurationOptionsTester Tester { get; set; }
    }
}