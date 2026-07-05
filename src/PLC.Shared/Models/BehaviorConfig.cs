namespace PLC.Shared.Models;

public class BehaviorConfig
{
    public int BaseLatencyMs { get; set; } = 5;
    public int JitterMs { get; set; } = 2;
    public int ScanCycleMs { get; set; } = 10;
    public FaultInjectionConfig? FaultInjection { get; set; }
}

public class FaultInjectionConfig
{
    public double DelayRate { get; set; }
    public int DelayMinMs { get; set; }
    public int DelayMaxMs { get; set; }
    public double DropRate { get; set; }
    public double ErrorRate { get; set; }
    public bool Enabled { get; set; }
}
