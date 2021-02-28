namespace CYPCore.Models
{
    public class NodeMonitorConfigurationOptions
    {
        public static string ConfigurationSectionName = "NodeMonitor";

        public bool Enabled { get; set; }
        public string Listening { get; set; }
        public ushort Port { get; set; }
    }
}