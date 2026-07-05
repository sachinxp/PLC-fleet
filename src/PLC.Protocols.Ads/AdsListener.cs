using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.Ads;

public class AdsListener : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<AdsSession> _sessions = new();
    private readonly int _maxConnections;
    private readonly int _baseLatencyMs;
    private readonly int _jitterMs;
    private AdsHandler _handler = null!;
    private readonly Random _rng = new();

    public AdsListener(List<TagDefinition> tags, int maxConnections = 8, int baseLatencyMs = 5, int jitterMs = 2, string deviceName = "TwinCAT 3 PLC", string version = "3.1.4024.56")
    {
        _maxConnections = maxConnections;
        _baseLatencyMs = baseLatencyMs;
        _jitterMs = jitterMs;
        _handler = new AdsHandler(tags, deviceName, version);
    }

    public AdsHandler Handler => _handler;

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
            try { var client = await _listener!.AcceptTcpClientAsync(ct);
                lock (_sessions) { if (_sessions.Count >= _maxConnections) { client.Close(); continue; } }
                var session = new AdsSession(client, this, _handler);
                lock (_sessions) { _sessions.Add(session); }
                _ = Task.Run(() => session.HandleAsync(ct));
            } catch (OperationCanceledException) { break; } catch (ObjectDisposedException) { break; }
        }
    }

    public int GetLatencyMs() { lock (_rng) { return _baseLatencyMs + (_jitterMs > 0 ? _rng.Next(0, _jitterMs + 1) : 0); } }
    public void RemoveSession(AdsSession session) { lock (_sessions) { _sessions.Remove(session); } }
    public int ActiveConnections { get { lock (_sessions) { return _sessions.Count; } } }

    public void Stop()
    {
        _cts?.Cancel(); _listener?.Stop();
        lock (_sessions) { foreach (var s in _sessions) s.Dispose(); _sessions.Clear(); }
    }
    public void Dispose() { Stop(); _cts?.Dispose(); }
}

public class AdsSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly AdsListener _listener;
    private readonly AdsHandler _handler;
    private readonly NetworkStream _stream;

    public AdsSession(TcpClient client, AdsListener listener, AdsHandler handler)
    {
        _client = client; _listener = listener; _handler = handler;
        _stream = client.GetStream();
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                // Read 4-byte length prefix
                var read = await _stream.ReadAsync(buffer, 0, 4, ct);
                if (read < 4) break;

                if (!AdsTcpLayer.TryParse(buffer[..4], out var frameLen, out _))
                {
                    // Need to read more to get full frame
                    var amsLen = (int)BitConverter.ToUInt32(buffer, 0);
                    var totalRead = 0;
                    while (totalRead < amsLen)
                    {
                        read = await _stream.ReadAsync(buffer, totalRead, amsLen - totalRead, ct);
                        if (read <= 0) break;
                        totalRead += read;
                    }
                    if (totalRead < amsLen) break;
                    var amsPacket = buffer[..totalRead].ToArray();
                    var pkt = AmsPacket.Parse(amsPacket);
                    if (pkt == null) break;

                    var latency = _listener.GetLatencyMs();
                    if (latency > 0) await Task.Delay(latency, ct);

                    var response = _handler.HandleRequest(pkt);
                    if (response != null)
                    {
                        var framed = AdsTcpLayer.Frame(response);
                        await _stream.WriteAsync(framed, ct);
                    }
                }
                else
                {
                    // Full frame already in buffer
                    var amsLen = (int)BitConverter.ToUInt32(buffer, 0);
                    read = await _stream.ReadAsync(buffer, 0, amsLen, ct);
                    if (read < amsLen) break;
                    var amsPacket = buffer[..read].ToArray();
                    var pkt = AmsPacket.Parse(amsPacket);
                    if (pkt == null) break;

                    var latency = _listener.GetLatencyMs();
                    if (latency > 0) await Task.Delay(latency, ct);

                    var response = _handler.HandleRequest(pkt);
                    if (response != null)
                    {
                        var framed = AdsTcpLayer.Frame(response);
                        await _stream.WriteAsync(framed, ct);
                    }
                }
            }
        }
        catch { }
        finally { _listener.RemoveSession(this); Dispose(); }
    }

    public void Dispose() { _stream?.Dispose(); _client?.Dispose(); }
}
