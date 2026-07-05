using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.Rockwell;

public class RockwellListener : IDisposable
{
    private TcpListener? _tcpListener;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private readonly List<RockwellSession> _sessions = new();
    private readonly int _maxConnections;
    private readonly int _baseLatencyMs;
    private readonly int _jitterMs;
    private readonly Random _rng = new();
    private readonly Dictionary<string, object?> _tagValues = new();
    private readonly List<TagDefinition> _tags;
    private uint _nextSessionHandle = 1;

    public RockwellListener(List<TagDefinition> tags, int maxConnections = 8, int baseLatencyMs = 5, int jitterMs = 2)
    {
        _tags = tags;
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
        _tcpListener = new TcpListener(address, port);
        _tcpListener.Start();

        // UDP ListIdentity
        _udpClient = new UdpClient(new IPEndPoint(address, port));
        _ = Task.Run(() => UdpListenLoopAsync(_cts.Token));
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener!.AcceptTcpClientAsync(ct);
                lock (_sessions)
                {
                    if (_sessions.Count >= _maxConnections) { client.Close(); continue; }
                }
                var handle = _nextSessionHandle++;
                var session = new RockwellSession(client, this, handle, _tags, _tagValues);
                lock (_sessions) { _sessions.Add(session); }
                _ = Task.Run(() => session.HandleAsync(ct));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task UdpListenLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _udpClient!.ReceiveAsync();
                var frame = EnipFrame.Parse(result.Buffer);
                if (frame != null && frame.Command == EnipFrame.ListIdentity)
                {
                    var resp = EnipFrame.BuildIdentityResponse();
                    await _udpClient.SendAsync(resp, resp.Length, result.RemoteEndPoint);
                }
            }
        }
        catch { }
    }

    public int GetLatencyMs()
    {
        lock (_rng) { return _baseLatencyMs + (_jitterMs > 0 ? _rng.Next(0, _jitterMs + 1) : 0); }
    }

    public void RemoveSession(RockwellSession session)
    {
        lock (_sessions) { _sessions.Remove(session); }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _tcpListener?.Stop();
        _udpClient?.Close();
        lock (_sessions) { foreach (var s in _sessions) s.Dispose(); _sessions.Clear(); }
    }

    public void Dispose() { Stop(); _cts?.Dispose(); _udpClient?.Dispose(); }
}
