using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using PLC.Shared.Ipc;
using PLC.Shared.Models;
using PLC.Shared.Serialization;
using Microsoft.Extensions.Logging;

public class PlcProcessManager
{
    private readonly ConcurrentDictionary<string, NamedPipeClient> _ipcClients = new();
    private readonly ILogger<PlcProcessManager> _logger;

    public PlcProcessManager(ILogger<PlcProcessManager> logger)
    {
        _logger = logger;
    }

    public async Task StartWorkerAsync(PlcInstance plc)
    {
        var pipeName = $"PLCWorker_{plc.Id}";
        var workerDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "PLC.Worker", "bin", "Debug", "net8.0", "PLC.Worker.dll");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{workerDll}\" --instance {plc.Id} --pipe {pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }
        };

        process.Start();
        _logger.LogInformation("Started worker process for PLC {Name} (PID: {Pid})", plc.Name, process.Id);

        // Connect IPC
        var client = new NamedPipeClient(pipeName);
        var connected = false;
        for (int retry = 0; retry < 3 && !connected; retry++)
        {
            try
            {
                _logger.LogInformation("IPC connect attempt {Retry} for PLC {Name}", retry + 1, plc.Name);
                await client.ConnectAsync(10000);
                connected = true;
                _logger.LogInformation("IPC connected for PLC {Name}", plc.Name);
            }
            catch (TimeoutException) { _logger.LogWarning("IPC connect timeout retry {Retry} for PLC {Name}", retry + 1, plc.Name); }
            catch (Exception ex) { _logger.LogError(ex, "IPC connect error for PLC {Name}", plc.Name); throw; }
        }
        if (!connected) throw new TimeoutException($"Failed to connect to worker for PLC {plc.Name} after 3 retries");
        _ipcClients[plc.Id] = client;

        // Send config
        _logger.LogInformation("Sending config to PLC {Name}", plc.Name);
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
        _logger.LogInformation("Config sent to PLC {Name}", plc.Name);

        // Wait for WorkerStarted
        _logger.LogInformation("Waiting for WorkerStarted from PLC {Name}", plc.Name);
        var response = await client.ReceiveAsync();
        if (response?.Type == IpcMessageType.WorkerStarted)
        {
            _logger.LogInformation("Worker for PLC {Name} confirmed started", plc.Name);
        }
    }

    public async Task StopWorkerAsync(string id)
    {
        if (_ipcClients.TryRemove(id, out var client))
        {
            var stopMsg = new IpcMessage
            {
                Type = IpcMessageType.StopWorker,
                Timestamp = Stopwatch.GetTimestamp()
            };
            await client.SendAsync(stopMsg);
            client.Dispose();
        }
    }
}
