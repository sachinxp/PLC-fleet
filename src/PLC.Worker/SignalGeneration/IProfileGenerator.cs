namespace PLC.Worker.SignalGeneration;

public interface IProfileGenerator
{
    object ComputeValue(long elapsedMs, object? previousValue);
}
