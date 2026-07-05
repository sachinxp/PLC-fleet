using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.OpcUa;

public class UaServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<UaSession> _sessions = new();
    private readonly int _maxConnections;
    private readonly int _baseLatencyMs;
    private readonly int _jitterMs;
    private readonly Random _rng = new();
    private readonly Dictionary<string, object?> _tagValues = new();
    private readonly List<TagDefinition> _tags;
    private readonly string _serverUri;
    private readonly string _manufacturer;
    private uint _nextSessionId = 1;

    public UaServer(List<TagDefinition> tags, string serverUri = "urn:plc-simulator:opcua", string manufacturer = "PLC Simulator",
        int maxConnections = 8, int baseLatencyMs = 5, int jitterMs = 2)
    {
        _tags = tags;
        _serverUri = serverUri;
        _manufacturer = manufacturer;
        _maxConnections = maxConnections;
        _baseLatencyMs = baseLatencyMs;
        _jitterMs = jitterMs;
    }

    public void UpdateTagValue(string name, object? value)
    {
        lock (_tagValues) { _tagValues[name] = value; }
    }

    public async Task StartAsync(IPAddress address, int port)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(address, port);
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                lock (_sessions) { if (_sessions.Count >= _maxConnections) { client.Close(); continue; } }
                var session = new UaSession(client, this, _nextSessionId++, _tags, _tagValues, _serverUri, _manufacturer);
                lock (_sessions) { _sessions.Add(session); }
                _ = Task.Run(() => session.HandleAsync(ct));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    public int GetLatencyMs() { lock (_rng) { return _baseLatencyMs + (_jitterMs > 0 ? _rng.Next(0, _jitterMs + 1) : 0); } }
    public void RemoveSession(UaSession session) { lock (_sessions) { _sessions.Remove(session); } }

    public void Stop() { _cts?.Cancel(); _listener?.Stop(); lock (_sessions) { foreach (var s in _sessions) s.Dispose(); _sessions.Clear(); } }
    public void Dispose() { Stop(); _cts?.Dispose(); }
}
