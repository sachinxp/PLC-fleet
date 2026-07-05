using System;
using System.Linq;

namespace PLC.Worker.SignalGeneration;

public class StaticProfile : IProfileGenerator
{
    private readonly double _value;
    public StaticProfile(PLC.Shared.Models.SimulationConfig config) => _value = config.Value;
    public object ComputeValue(long elapsedMs, object? previousValue) => _value;
}

public class StepProfile : IProfileGenerator
{
    private readonly double _step, _lowLimit, _highLimit, _updateMs;
    private readonly string _direction, _atLimit;
    public StepProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _step = config.Step; _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _updateMs = config.UpdateMs > 0 ? config.UpdateMs : 1000;
        _direction = config.Direction ?? "Up"; _atLimit = config.AtLimit ?? "AutoReverse";
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var ticks = elapsedMs / (long)_updateMs;
        var value = (previousValue as double? ?? _lowLimit) + (_direction == "Up" ? _step : -_step) * (ticks - (ticks - 1));
        if (value >= _highLimit)
        {
            value = _atLimit switch
            {
                "Wrap" => _lowLimit,
                "Clamp" => _highLimit,
                _ => value
            };
        }
        else if (value <= _lowLimit)
        {
            value = _atLimit switch
            {
                "Wrap" => _highLimit,
                "Clamp" => _lowLimit,
                _ => value
            };
        }
        return value;
    }
}

public class RampProfile : IProfileGenerator
{
    private readonly double _lowLimit, _highLimit, _periodMs, _updateMs;
    public RampProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _periodMs = config.PeriodMs > 0 ? config.PeriodMs : 10000;
        _updateMs = config.UpdateMs > 0 ? config.UpdateMs : 1000;
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var t = (elapsedMs % _periodMs) / _periodMs;
        return _lowLimit + t * (_highLimit - _lowLimit);
    }
}

public class SineProfile : IProfileGenerator
{
    private readonly double _lowLimit, _highLimit, _periodMs, _phaseRad, _noisePercent;
    private readonly Random _rng;
    public SineProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _periodMs = config.PeriodMs > 0 ? config.PeriodMs : 10000;
        _phaseRad = config.PhaseDeg * Math.PI / 180.0;
        _noisePercent = config.NoisePercent;
        _rng = config.Seed.HasValue ? new Random(config.Seed.Value) : new Random();
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var mid = (_lowLimit + _highLimit) / 2.0;
        var amp = (_highLimit - _lowLimit) / 2.0;
        var val = mid + amp * Math.Sin(2 * Math.PI * elapsedMs / _periodMs + _phaseRad);
        if (_noisePercent > 0)
        {
            var noise = amp * _noisePercent / 100.0 * (_rng.NextDouble() * 2 - 1);
            val += noise;
        }
        return val;
    }
}

public class CosineProfile : IProfileGenerator
{
    private readonly SineProfile _sine;
    public CosineProfile(PLC.Shared.Models.SimulationConfig config)
    {
        var c = new PLC.Shared.Models.SimulationConfig
        {
            LowLimit = config.LowLimit, HighLimit = config.HighLimit,
            PeriodMs = config.PeriodMs, PhaseDeg = config.PhaseDeg + 90,
            NoisePercent = config.NoisePercent, Seed = config.Seed
        };
        _sine = new SineProfile(c);
    }
    public object ComputeValue(long elapsedMs, object? previousValue) => _sine.ComputeValue(elapsedMs, previousValue);
}

public class SquareProfile : IProfileGenerator
{
    private readonly double _lowLimit, _highLimit, _periodMs, _dutyPercent;
    public SquareProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _periodMs = config.PeriodMs > 0 ? config.PeriodMs : 10000;
        _dutyPercent = config.DutyPercent > 0 ? config.DutyPercent : 50;
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var cyclePos = elapsedMs % _periodMs;
        return cyclePos < _periodMs * _dutyPercent / 100.0 ? _highLimit : _lowLimit;
    }
}

public class TriangleProfile : IProfileGenerator
{
    private readonly double _lowLimit, _highLimit, _periodMs;
    public TriangleProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _periodMs = config.PeriodMs > 0 ? config.PeriodMs : 10000;
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var t = (elapsedMs % _periodMs) / _periodMs;
        return _lowLimit + 2 * (t < 0.5 ? t : 1 - t) * (_highLimit - _lowLimit);
    }
}

public class RandomProfile : IProfileGenerator
{
    private readonly double _lowLimit, _highLimit;
    private readonly string _distribution;
    private readonly Random _rng;
    public RandomProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _distribution = config.Distribution ?? "uniform";
        _rng = config.Seed.HasValue ? new Random(config.Seed.Value) : new Random();
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        if (_distribution == "normal")
        {
            var u1 = 1.0 - _rng.NextDouble();
            var u2 = 1.0 - _rng.NextDouble();
            var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            var mean = (_lowLimit + _highLimit) / 2.0;
            var std = (_highLimit - _lowLimit) / 6.0;
            return Math.Clamp(mean + normal * std, _lowLimit, _highLimit);
        }
        return _lowLimit + _rng.NextDouble() * (_highLimit - _lowLimit);
    }
}

public class NoiseProfile : IProfileGenerator
{
    private readonly double _baseValue, _noisePercent, _lowLimit, _highLimit;
    private readonly Random _rng;
    public NoiseProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _baseValue = config.Value;
        _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _noisePercent = config.NoisePercent;
        _rng = new Random();
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var amp = (_highLimit - _lowLimit) * _noisePercent / 100.0;
        var val = (_baseValue as double? ?? _baseValue) + amp * (_rng.NextDouble() * 2 - 1);
        return Math.Clamp(val, _lowLimit, _highLimit);
    }
}

public class RandomWalkProfile : IProfileGenerator
{
    private readonly double _step, _lowLimit, _highLimit;
    private readonly Random _rng;
    public RandomWalkProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _step = config.Step > 0 ? config.Step : 1;
        _lowLimit = config.LowLimit; _highLimit = config.HighLimit;
        _rng = new Random();
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var current = previousValue as double? ?? _lowLimit;
        current += (_rng.NextDouble() * 2 - 1) * _step;
        return Math.Clamp(current, _lowLimit, _highLimit);
    }
}

public class ToggleProfile : IProfileGenerator
{
    private readonly int _intervalMs;
    public ToggleProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _intervalMs = config.IntervalMs > 0 ? config.IntervalMs : 1000;
    }
    public object ComputeValue(long elapsedMs, object? previousValue) =>
        (elapsedMs / _intervalMs) % 2 == 0;
}

public class PulseProfile : IProfileGenerator
{
    private readonly int _periodMs;
    private readonly double _dutyPercent;
    public PulseProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _periodMs = config.PeriodMs > 0 ? config.PeriodMs : 10000;
        _dutyPercent = config.DutyPercent > 0 ? config.DutyPercent : 50;
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var cyclePos = elapsedMs % _periodMs;
        return cyclePos < _periodMs * _dutyPercent / 100.0;
    }
}

public class CounterProfile : IProfileGenerator
{
    private readonly double _step;
    private readonly double _rolloverAt;
    public CounterProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _step = config.Step > 0 ? config.Step : 1;
        _rolloverAt = config.RolloverAt > 0 ? config.RolloverAt : double.MaxValue;
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var current = previousValue as double? ?? 0;
        current += _step;
        if (current >= _rolloverAt) current = 0;
        return current;
    }
}

public class ClockProfile : IProfileGenerator
{
    private readonly string _format;
    public ClockProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _format = config.Format ?? "HH:mm:ss";
    }
    public object ComputeValue(long elapsedMs, object? previousValue) =>
        DateTime.UtcNow.ToString(_format);
}

public class TextCycleProfile : IProfileGenerator
{
    private readonly string[] _values;
    private readonly int _intervalMs;
    public TextCycleProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _values = config.Values?.ToArray() ?? new[] { "A", "B" };
        _intervalMs = config.IntervalMs > 0 ? config.IntervalMs : 5000;
    }
    public object ComputeValue(long elapsedMs, object? previousValue)
    {
        var idx = (elapsedMs / _intervalMs) % _values.Length;
        return _values[idx];
    }
}

public class EchoProfile : IProfileGenerator
{
    private readonly object _initialValue;
    public EchoProfile(PLC.Shared.Models.SimulationConfig config)
    {
        _initialValue = config.Value;
    }
    public object ComputeValue(long elapsedMs, object? previousValue) =>
        previousValue ?? _initialValue;
}
