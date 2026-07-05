namespace PLC.Shared.Models;

public enum Brand
{
    Siemens,
    Rockwell,
    Modbus,
    Mitsubishi,
    Beckhoff,
    OpcUa
}

public static class BrandExtensions
{
    public static string ToProtocolId(this Brand brand) => brand switch
    {
        Brand.Siemens => "s7",
        Brand.Rockwell => "rockwell",
        Brand.Modbus => "modbus-tcp",
        Brand.Mitsubishi => "melsec",
        Brand.Beckhoff => "ads",
        Brand.OpcUa => "opcua",
        _ => "unknown"
    };

    public static int DefaultPort(this Brand brand) => brand switch
    {
        Brand.Siemens => 102,
        Brand.Rockwell => 44818,
        Brand.Modbus => 502,
        Brand.Mitsubishi => 5007,
        Brand.Beckhoff => 48898,
        Brand.OpcUa => 4840,
        _ => 0
    };

    public static string[] SupportedPersonalities(this Brand brand) => brand switch
    {
        Brand.Siemens => new[] { "s7-300", "s7-400", "s7-1200", "s7-1500" },
        Brand.Rockwell => new[] { "controllogix-l7x", "compactlogix-l33er" },
        Brand.Modbus => new[] { "generic", "m340" },
        Brand.Mitsubishi => new[] { "fx5u", "q03ude" },
        Brand.Beckhoff => new[] { "twincat-3", "twincat-2" },
        Brand.OpcUa => new[] { "generic-server", "s7-1500-flavored" },
        _ => System.Array.Empty<string>()
    };
}
