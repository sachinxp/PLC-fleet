using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using PLC.Protocols.Modbus;
using PLC.Protocols.S7;
using PLC.Protocols.Rockwell;
using PLC.Protocols.Melsec;
using PLC.Protocols.Ads;
using PLC.Protocols.OpcUa;
using PLC.Shared.Ipc;
using PLC.Shared.Models;
using PLC.Shared.Serialization;
using PLC.Worker.SignalGeneration;

class WorkerProgram
{
    static async Task Main(string[] args)
    {
        var instanceId = GetArg(args, "--instance") ?? "unknown";
        var pipeName = GetArg(args, "--pipe") ?? $"PLCWorker_{instanceId}";

        var server = new NamedPipeServer(pipeName);
        await server.WaitForConnectionAsync();

        var msg = await server.ReceiveAsync();
        PlcInstance? plc = null;
        if (msg?.Type == IpcMessageType.ConfigSnapshot)
        {
            var config = ConfigSerializer.Deserialize<ConfigSnapshotPayload>(msg.Payload);
            plc = config?.Instance;
        }

        if (plc == null)
        {
            return;
        }

        var tagValues = new ConcurrentDictionary<string, object?>();
        var writePolicy = new ConcurrentDictionary<string, bool>();
        foreach (var tag in plc.Tags.Where(t => t.Enabled))
        {
            tagValues[tag.Name] = tag.Simulation.Value;
        }

        var generators = new ConcurrentDictionary<string, IProfileGenerator>();
        foreach (var tag in plc.Tags.Where(t => t.Enabled))
        {
            generators[tag.Name] = ProfileFactory.Create(tag.Simulation);
        }

        var bindIp = IPAddress.TryParse(plc.Network.IpAddress, out var parsed) ? parsed : IPAddress.Any;

        ModbusListener? modbusListener = null;
        S7Listener? s7Listener = null;
        RockwellListener? rockwellListener = null;
        MelsecListener? melsecListener = null;
        AdsListener? adsListener = null;
        UaServer? opcuaServer = null;

        if (plc.Brand == Brand.Modbus)
        {
            modbusListener = new ModbusListener(plc.Network.MaxConnections, plc.Behavior.BaseLatencyMs, plc.Behavior.JitterMs);
            modbusListener.Initialize(plc.Tags);
            await modbusListener.StartAsync(bindIp, plc.Network.Port);
        }
        else if (plc.Brand == Brand.Siemens)
        {
            s7Listener = new S7Listener(plc.Network.MaxConnections, plc.Behavior.BaseLatencyMs, plc.Behavior.JitterMs);
            s7Listener.Initialize(plc);
            await s7Listener.StartAsync(bindIp, plc.Network.Port);
        }
        else if (plc.Brand == Brand.Rockwell)
        {
            rockwellListener = new RockwellListener(plc.Tags, plc.Network.MaxConnections, plc.Behavior.BaseLatencyMs, plc.Behavior.JitterMs);
            await rockwellListener.StartAsync(bindIp, plc.Network.Port);
        }
        else if (plc.Brand == Brand.Mitsubishi)
        {
            melsecListener = new MelsecListener(plc.Tags, plc.Personality == "q03ude" ? "Q03UDE" : "FX5U-32MT/ES",
                plc.Network.MaxConnections, plc.Behavior.BaseLatencyMs, plc.Behavior.JitterMs);
            await melsecListener.StartAsync(bindIp, plc.Network.Port);
        }
        else if (plc.Brand == Brand.Beckhoff)
        {
            adsListener = new AdsListener(plc.Tags, plc.Network.MaxConnections, plc.Behavior.BaseLatencyMs, plc.Behavior.JitterMs,
                plc.Personality == "twincat-2" ? "TwinCAT 2 PLC" : "TwinCAT 3 PLC", "3.1.4024.56");
            await adsListener.StartAsync(bindIp, plc.Network.Port);
        }
        else if (plc.Brand == Brand.OpcUa)
        {
            opcuaServer = new UaServer(plc.Tags, $"urn:{plc.Name.ToLowerInvariant()}:opcua", "PLC Simulator",
                plc.Network.MaxConnections, plc.Behavior.BaseLatencyMs, plc.Behavior.JitterMs);
            await opcuaServer.StartAsync(bindIp, plc.Network.Port);
        }

        // Push initial values to protocol handlers
        foreach (var tag in plc.Tags.Where(t => t.Enabled))
        {
            var initialVal = tagValues[tag.Name];
            if (initialVal != null)
            {
                modbusListener?.Handler.UpdateTagValue(tag.Name, initialVal);
                s7Listener?.Handler.UpdateTagValue(tag.Name, initialVal);
                rockwellListener?.UpdateTagValue(tag.Name, initialVal);
                melsecListener?.Handler.UpdateTagValue(tag.Name, initialVal);
                adsListener?.Handler.UpdateTagValue(tag.Name, initialVal);
                opcuaServer?.UpdateTagValue(tag.Name, initialVal);
            }
        }

        var scheduler = new SignalScheduler();
        foreach (var tag in plc.Tags.Where(t => t.Enabled && t.Simulation.Profile != "Echo"))
        {
            var tagName = tag.Name;
            var profile = generators[tagName];
            scheduler.Register(tagName, tag.Simulation.UpdateMs > 0 ? tag.Simulation.UpdateMs : 1000, (elapsedMs) =>
            {
                if (writePolicy.TryGetValue(tagName, out var paused) && paused) return;
                var newVal = profile.ComputeValue(elapsedMs, tagValues[tagName]);
                tagValues[tagName] = newVal;
                if (newVal != null)
                {
                    modbusListener?.Handler.UpdateTagValue(tagName, newVal);
                    s7Listener?.Handler.UpdateTagValue(tagName, newVal);
                    rockwellListener?.UpdateTagValue(tagName, newVal);
                    melsecListener?.Handler.UpdateTagValue(tagName, newVal);
                    adsListener?.Handler.UpdateTagValue(tagName, newVal);
                    opcuaServer?.UpdateTagValue(tagName, newVal);
                }
            });
        }

        await server.SendAsync(new IpcMessage
        {
            Type = IpcMessageType.WorkerStarted,
            Timestamp = DateTime.UtcNow.Ticks
        });

        var running = true;
        while (running)
        {
            var request = await server.ReceiveAsync();
            if (request == null) break;

            switch (request.Type)
            {
                case IpcMessageType.StopWorker:
                    running = false;
                    modbusListener?.Stop();
                    s7Listener?.Stop();
                    rockwellListener?.Stop();
                    melsecListener?.Stop();
                    adsListener?.Stop();
                    opcuaServer?.Stop();
                    scheduler.Dispose();
                    await server.SendAsync(new IpcMessage
                    {
                        Type = IpcMessageType.WorkerStopped,
                        Timestamp = DateTime.UtcNow.Ticks
                    });
                    break;

                case IpcMessageType.UpdateTag:
                    break;

                case IpcMessageType.Ping:
                    await server.SendAsync(new IpcMessage
                    {
                        Type = IpcMessageType.Pong,
                        Timestamp = DateTime.UtcNow.Ticks
                    });
                    break;
            }
        }

        scheduler.Dispose();
        modbusListener?.Dispose();
        s7Listener?.Dispose();
        rockwellListener?.Dispose();
        melsecListener?.Dispose();
        adsListener?.Dispose();
        opcuaServer?.Dispose();
        server.Dispose();
    }

    static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}