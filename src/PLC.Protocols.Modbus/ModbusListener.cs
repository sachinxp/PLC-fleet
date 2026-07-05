using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.Modbus;

public class ModbusListener : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<ModbusSession> _sessions = new();
    private readonly int _maxConnections;
    private readonly int _baseLatencyMs;
    private readonly int _jitterMs;
    private ModbusRequestHandler _handler = null!;
    private readonly Random _rng = new();

    public ModbusListener(int maxConnections = 8, int baseLatencyMs = 5, int jitterMs = 2)
    {
        _maxConnections = maxConnections;
        _baseLatencyMs = baseLatencyMs;
        _jitterMs = jitterMs;
    }

    public void Initialize(List<TagDefinition> tags)
    {
        var addressMap = new ModbusAddressMap(tags);
        _handler = new ModbusRequestHandler(addressMap);
    }

    public ModbusRequestHandler Handler => _handler;

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
                lock (_sessions)
                {
                    if (_sessions.Count >= _maxConnections)
                    {
                        client.Close();
                        continue;
                    }
                }
                var session = new ModbusSession(client, this, _handler);
                lock (_sessions) { _sessions.Add(session); }
                _ = Task.Run(() => session.HandleAsync(ct));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    public int GetLatencyMs()
    {
        lock (_rng)
        {
            return _baseLatencyMs + (_jitterMs > 0 ? _rng.Next(0, _jitterMs + 1) : 0);
        }
    }

    public void RemoveSession(ModbusSession session)
    {
        lock (_sessions) { _sessions.Remove(session); }
    }

    public int ActiveConnections
    {
        get { lock (_sessions) { return _sessions.Count; } }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        List<ModbusSession> sessions;
        lock (_sessions) { sessions = new List<ModbusSession>(_sessions); }
        foreach (var s in sessions) s.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
