namespace CypherNetwork.Models;

public record NodeMonitorConfigurationOptionsTester
{
    public string Listening { get; set; }
    public ushort Port { get; set; }
}

public record NodeMonitorConfigurationOptions
{
    public static string ConfigurationSectionName = "NodeMonitor";

    public bool Enabled { get; set; }

    public NodeMonitorConfigurationOptionsTester Tester { get; set; }
}