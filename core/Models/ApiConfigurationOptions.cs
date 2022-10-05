namespace CypherNetwork.Models;

public record RemoteNode(byte[] IpAddress, byte[] TcpPort, byte[] PublicKey);