namespace CypherNetwork.Models;

public record AppOptions
{
    public string Name { get; set; }
    public string HttpEndPoint { get; set; }
    public int HttpsPort { get; set; }
    public GossipOptions Gossip { get; set; }
    public DataOptions Data { get; set; }
    public StakingOptions Staking { get; set; }
    public NetworkSetting Network { get; set; }
}

public record GossipOptions
{
    public byte[] Listening { get; set; }
    public byte[] Advertise { get; set; }
    public Node[] Seeds { get; set; }
}

public record Node
{
    public byte[] Advertise { get; set; }
    public byte[] Listening { get; set; }
}

public record DataOptions
{
    public string RocksDb { get; set; }
    public string KeysProtectionPath { get; set; }
}