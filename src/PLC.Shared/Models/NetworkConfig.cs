namespace PLC.Shared.Models;

public class NetworkConfig
{
    public string NicName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "127.0.0.2";
    public string SubnetMask { get; set; } = "255.255.255.0";
    public int Port { get; set; }
    public int MaxConnections { get; set; } = 8;
    public bool UseUdp { get; set; }
    public bool UseLoopback { get; set; } = true;
}
