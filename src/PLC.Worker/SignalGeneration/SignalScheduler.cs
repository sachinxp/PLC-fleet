using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace PLC.Worker.SignalGeneration;

public class SignalScheduler : IDisposable
{
    private const int TickIntervalMs = 50;
    private readonly ConcurrentDictionary<string, TimerEntry> _entries = new();
    private readonly Timer _timer;
    private readonly long _startTime;

    public SignalScheduler()
    {
        _startTime = Stopwatch.GetTimestamp();
        _timer = new Timer(Tick, null, TickIntervalMs, TickIntervalMs);
    }

    private void Tick(object? state)
    {
        var now = (Stopwatch.GetTimestamp() - _startTime) * 1000 / Stopwatch.Frequency;
        foreach (var kvp in _entries)
        {
            var entry = kvp.Value;
            if (entry.NextTick <= now)
            {
                entry.NextTick = now + entry.UpdateMs;
                try { entry.Callback(now); }
                catch { }
            }
        }
    }

    public void Register(string name, int updateMs, Action<long> callback)
    {
        var now = (Stopwatch.GetTimestamp() - _startTime) * 1000 / Stopwatch.Frequency;
        _entries[name] = new TimerEntry
        {
            Name = name,
            UpdateMs = Math.Max(updateMs, TickIntervalMs),
            NextTick = now + Math.Max(updateMs, TickIntervalMs),
            Callback = callback
        };
    }

    public void Unregister(string name) => _entries.TryRemove(name, out _);

    public void Dispose() => _timer?.Dispose();

    private class TimerEntry
    {
        public string Name { get; set; } = "";
        public int UpdateMs { get; set; }
        public long NextTick { get; set; }
        public Action<long> Callback { get; set; } = _ => { };
    }
}
