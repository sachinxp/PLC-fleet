using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PLC.Shared.Models;
using PLC.Shared.Serialization;

public class FleetService
{
    private readonly ConcurrentDictionary<string, PlcInstance> _instances = new();
    private readonly ConfigPersistence _persistence;
    private readonly PlcProcessManager _processManager;
    private static readonly string FleetDir = Path.Combine("data", "fleet");

    public FleetService(ConfigPersistence persistence, PlcProcessManager processManager)
    {
        _persistence = persistence;
        _processManager = processManager;
        if (!Directory.Exists(FleetDir)) Directory.CreateDirectory(FleetDir);

        _processManager.WorkerCrashed += id => MarkCrashed(id);
        _processManager.WorkerRestarted += id => MarkRestarted(id);
    }

    private void MarkCrashed(string id)
    {
        if (_instances.TryGetValue(id, out var plc))
        {
            plc.State = PlcState.Error;
            plc.ErrorCount++;
            _persistence.SavePlc(plc);
        }
    }

    private void MarkRestarted(string id)
    {
        if (_instances.TryGetValue(id, out var plc))
        {
            plc.State = PlcState.Running;
            _persistence.SavePlc(plc);
        }
    }

    public IReadOnlyCollection<PlcInstance> GetAll() => _instances.Values.ToList().AsReadOnly();

    public PlcInstance? Get(string id) => _instances.TryGetValue(id, out var plc) ? plc : null;

    public PlcInstance Create(PlcInstance template)
    {
        template.Id = Guid.NewGuid().ToString("N")[..12];
        template.State = PlcState.Created;
        _instances[template.Id] = template;
        _persistence.SavePlc(template);
        return template;
    }

    public async Task<bool> StartAsync(string id)
    {
        if (!_instances.TryGetValue(id, out var plc)) return false;
        plc.State = PlcState.Running;
        await _processManager.StartWorkerAsync(plc);
        _persistence.SavePlc(plc);
        return true;
    }

    public async Task<bool> StopAsync(string id)
    {
        if (!_instances.TryGetValue(id, out var plc)) return false;
        plc.State = PlcState.Stopped;
        await _processManager.StopWorkerAsync(id);
        _persistence.SavePlc(plc);
        return true;
    }

    public bool Delete(string id)
    {
        if (!_instances.TryRemove(id, out var plc)) return false;
        _processManager.StopWorkerAsync(id).ConfigureAwait(false);
        var path = Path.Combine(FleetDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
        return true;
    }

    public void Update(PlcInstance plc)
    {
        _instances[plc.Id] = plc;
        _persistence.SavePlc(plc);
    }

    public async Task LoadFromDiskAsync()
    {
        if (!Directory.Exists(FleetDir)) return;
        foreach (var file in Directory.GetFiles(FleetDir, "*.json"))
        {
            var plc = ConfigSerializer.LoadFromFile<PlcInstance>(file);
            if (plc != null)
            {
                plc.State = PlcState.Stopped;
                _instances[plc.Id] = plc;
            }
        }
        await Task.CompletedTask;
    }

    public string SuggestNextIp()
    {
        var used = _instances.Values
            .Select(p => p.Network.IpAddress)
            .Where(ip => System.Net.IPAddress.TryParse(ip, out _))
            .ToHashSet();
        
        for (int i = 2; i < 255; i++)
        {
            var candidate = $"127.0.0.{i}";
            if (!used.Contains(candidate)) return candidate;
        }
        return "127.0.0.2";
    }
}
