using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PLC.Shared.Models;

public class TagDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = "Int16";
    public TagAccess Access { get; set; } = TagAccess.ReadWrite;
    public string Description { get; set; } = string.Empty;
    public string EngUnit { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public SimulationConfig Simulation { get; set; } = new();
}

public class SimulationConfig
{
    public string Profile { get; set; } = "Static";
    public double Value { get; set; }
    public double Step { get; set; } = 1;
    public string Direction { get; set; } = "Up";
    public double LowLimit { get; set; }
    public double HighLimit { get; set; } = 100;
    public int PeriodMs { get; set; } = 10000;
    public int UpdateMs { get; set; } = 1000;
    public string AtLimit { get; set; } = "AutoReverse";
    public double PhaseDeg { get; set; }
    public double NoisePercent { get; set; }
    public double DutyPercent { get; set; } = 50;
    public string Distribution { get; set; } = "Uniform";
    public int IntervalMs { get; set; } = 1000;
    public int RolloverAt { get; set; }
    public string Format { get; set; } = "HH:mm:ss";
    public List<string> Values { get; set; } = new();
    public int? Seed { get; set; }
    public string WritePolicy { get; set; } = "Override";
}
