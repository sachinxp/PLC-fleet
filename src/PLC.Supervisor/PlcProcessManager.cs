using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Ipc;
using PLC.Shared.Models;
using PLC.Shared.Serialization;
using Microsoft.Extensions.Logging;

public class PlcProcessManager : IDisposable
{
    private class WorkerState
    {
        public Process Process { get; set; } = null!;
        public NamedPipeClient Client { get; set; } = null!;
        public PlcInstance Plc { get; set; } = null!;
        public bool Stopping { get; set; }
        public int RestartCount { get; set; }
        public DateTime LastPong { get; set; }
        public CancellationTokenSource Cts { get; set; } = new();
    }

    private readonly ConcurrentDictionary<string, WorkerState> _workers = new();
    private readonly ILogger<PlcProcessManager> _logger;
    private readonly Timer _healthTimer;

    public event Action<string>? WorkerCrashed;
    public event Action<string>? WorkerRestarted;

    private static readonly string WorkerDll = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
        "PLC.Worker", "bin", "Debug", "net8.0", "PLC.Worker.dll");

    public PlcProcessManager(ILogger<PlcProcessManager> logger)
    {
        _logger = logger;
        _healthTimer = new Timer(DoHealthCheck, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public async Task StartWorkerAsync(PlcInstance plc)
    {
        var state = await LaunchWorkerAsync(plc);
        if (state != null)
        {
            _workers[plc.Id] = state;
            WorkerRestarted?.Invoke(plc.Id);
        }
    }

    private async Task<WorkerState?> LaunchWorkerAsync(PlcInstance plc)
    {
        var pipeName = $"PLCWorker_{plc.Id}";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{WorkerDll}\" --instance {plc.Id} --pipe {pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            },
            EnableRaisingEvents = true
        };

        process.Start();
        _logger.LogInformation("Started worker process for PLC {Name} (PID: {Pid})", plc.Name, process.Id);

        var client = new NamedPipeClient(pipeName);
        var connected = false;
        for (int retry = 0; retry < 3 && !connected; retry++)
        {
            try
            {
                await client.ConnectAsync(10000);
                connected = true;
            }
            catch (TimeoutException) { _logger.LogWarning("IPC connect timeout retry {Retry} for PLC {Name}", retry + 1, plc.Name); }
            catch (Exception ex) { _logger.LogError(ex, "IPC connect error for PLC {Name}", plc.Name); throw; }
        }
        if (!connected)
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
            client.Dispose();
            throw new TimeoutException($"Failed to connect to worker for PLC {plc.Name} after 3 retries");
        }

        var configMsg = new IpcMessage
        {
            Type = IpcMessageType.ConfigSnapshot,
            Payload = ConfigSerializer.Serialize(new ConfigSnapshotPayload
            {
                InstanceId = plc.Id,
                Instance = plc
            }),
            Timestamp = Stopwatch.GetTimestamp()
        };
        await client.SendAsync(configMsg);

        var response = await client.ReceiveAsync();
        if (response?.Type != IpcMessageType.WorkerStarted)
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
            client.Dispose();
            throw new InvalidOperationException($"Worker for PLC {plc.Name} did not confirm start");
        }

        _logger.LogInformation("Worker for PLC {Name} confirmed started", plc.Name);

        var state = new WorkerState
        {
            Process = process,
            Client = client,
            Plc = plc,
            LastPong = DateTime.UtcNow
        };

        process.Exited += (_, _) => OnWorkerExited(state);
        return state;
    }

    private void OnWorkerExited(WorkerState state)
    {
        var id = state.Plc.Id;
        _logger.LogWarning("Worker process for PLC {Name} exited (PID: {Pid})", state.Plc.Name, state.Process.Id);

        if (!state.Stopping)
        {
            state.Plc.ErrorCount++;
            WorkerCrashed?.Invoke(id);
            _ = ScheduleRestartAsync(state);
        }
    }

    private async Task ScheduleRestartAsync(WorkerState state)
    {
        var id = state.Plc.Id;
        // Remove old state (keep it as a local reference for restart info)
        _workers.TryRemove(id, out _);

        // Exponential backoff: 1s, 2s, 4s, 8s, ... max 30s
        var delay = Math.Min(1000 * (1 << Math.Min(state.RestartCount, 5)), 30000);
        state.RestartCount++;
        _logger.LogInformation("Scheduling restart for PLC {Name} in {Delay}ms (attempt #{Attempt})",
            state.Plc.Name, delay, state.RestartCount);

        try
        {
            await Task.Delay(delay, state.Cts.Token);
            if (state.Cts.IsCancellationRequested) return;
        }
        catch (OperationCanceledException) { return; }

        // Clean up old resources
        state.Client.Dispose();
        state.Process.Dispose();

        try
        {
            var newState = await LaunchWorkerAsync(state.Plc);
            if (newState != null)
            {
                newState.Stopping = state.Stopping;
                newState.RestartCount = state.RestartCount;
                _workers[id] = newState;
                WorkerRestarted?.Invoke(id);
                _logger.LogInformation("Worker for PLC {Name} restarted successfully (attempt #{Attempt})",
                    state.Plc.Name, newState.RestartCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart worker for PLC {Name} (attempt #{Attempt})",
                state.Plc.Name, state.RestartCount);
            if (state.RestartCount < 10)
                _ = ScheduleRestartAsync(state);
            else
                _logger.LogCritical("Worker for PLC {Name} failed to restart after 10 attempts, giving up",
                    state.Plc.Name);
        }
    }

    private void DoHealthCheck(object? _)
    {
        foreach (var kvp in _workers)
        {
            var state = kvp.Value;
            if (state.Stopping) continue;

            // Check if process is still alive
            if (state.Process.HasExited)
            {
                OnWorkerExited(state);
                continue;
            }

            // Send ping
            try
            {
                _ = state.Client.SendAsync(new IpcMessage
                {
                    Type = IpcMessageType.Ping,
                    Timestamp = Stopwatch.GetTimestamp()
                });
            }
            catch
            {
                // Pipe broken - will be handled by process exit
            }
        }
    }

    public async Task StopWorkerAsync(string id)
    {
        if (_workers.TryRemove(id, out var state))
        {
            state.Stopping = true;
            state.Cts.Cancel();

            try
            {
                await state.Client.SendAsync(new IpcMessage { Type = IpcMessageType.StopWorker, Timestamp = Stopwatch.GetTimestamp() });

                // Wait up to 3 seconds for graceful shutdown
                if (!state.Process.WaitForExit(3000))
                {
                    _logger.LogWarning("Worker for PLC {Name} did not exit gracefully, killing", state.Plc.Name);
                    state.Process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping worker for PLC {Name}", state.Plc.Name);
                try { state.Process.Kill(entireProcessTree: true); } catch { }
            }

            state.Client.Dispose();
            state.Process.Dispose();
        }
    }

    public void Dispose()
    {
        _healthTimer.Dispose();
        foreach (var kvp in _workers)
        {
            var state = kvp.Value;
            state.Stopping = true;
            state.Cts.Cancel();
            try { state.Process.Kill(entireProcessTree: true); } catch { }
            state.Client.Dispose();
            state.Process.Dispose();
            state.Cts.Dispose();
        }
        _workers.Clear();
    }
}
