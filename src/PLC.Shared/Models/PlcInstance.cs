using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PLC.Shared.Models;

public class PlcInstance
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Brand Brand { get; set; }
    public string Personality { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PlcState State { get; set; } = PlcState.Created;
    public NetworkConfig Network { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();
    public List<TagDefinition> Tags { get; set; } = new();
    public string OrderCode { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public int ActiveConnections { get; set; }
    public long RequestsServed { get; set; }
    public int ErrorCount { get; set; }
}
