namespace CYPCore.Models
{
    public class NodeMonitorConfigurationOptionsTester
    {
        public bool Enabled { get; set; }
        public string Listening { get; set; }
        public ushort Port { get; set; }
    }

    public class NodeMonitorConfigurationOptions
    {
        public static string ConfigurationSectionName = "NodeMonitor";

        public NodeMonitorConfigurationOptionsTester Tester { get; set; }
    }
}