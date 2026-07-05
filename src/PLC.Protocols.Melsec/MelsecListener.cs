using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.Melsec;

public class MelsecListener : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<MelsecSession> _sessions = new();
    private readonly int _maxConnections;
    private readonly int _baseLatencyMs;
    private readonly int _jitterMs;
    private MelsecHandler _handler = null!;
    private readonly Random _rng = new();

    public MelsecListener(List<TagDefinition> tags, string modelName = "FX5U-32MT/ES", int maxConnections = 8, int baseLatencyMs = 5, int jitterMs = 2)
    {
        _maxConnections = maxConnections;
        _baseLatencyMs = baseLatencyMs;
        _jitterMs = jitterMs;
        _handler = new MelsecHandler(tags, modelName);
    }

    public MelsecHandler Handler => _handler;

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
                    if (_sessions.Count >= _maxConnections) { client.Close(); continue; }
                }
                var session = new MelsecSession(client, this, _handler);
                lock (_sessions) { _sessions.Add(session); }
                _ = Task.Run(() => session.HandleAsync(ct));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    public int GetLatencyMs()
    {
        lock (_rng) { return _baseLatencyMs + (_jitterMs > 0 ? _rng.Next(0, _jitterMs + 1) : 0); }
    }

    public void RemoveSession(MelsecSession session)
    {
        lock (_sessions) { _sessions.Remove(session); }
    }

    public int ActiveConnections { get { lock (_sessions) { return _sessions.Count; } } }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        lock (_sessions) { foreach (var s in _sessions) s.Dispose(); _sessions.Clear(); }
    }

    public void Dispose() { Stop(); _cts?.Dispose(); }
}

public class MelsecSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly MelsecListener _listener;
    private readonly MelsecHandler _handler;
    private readonly NetworkStream _stream;

    public MelsecSession(TcpClient client, MelsecListener listener, MelsecHandler handler)
    {
        _client = client;
        _listener = listener;
        _handler = handler;
        _stream = client.GetStream();
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                // Read MC header (14 bytes)
                var read = await _stream.ReadAsync(buffer, 0, McFrame.HeaderSize, ct);
                if (read < McFrame.HeaderSize) break;

                var headerBytes = buffer[..read].ToArray();
                var frame = McFrame.Parse(headerBytes);
                if (frame == null) break;

                // Read remaining data
                var remaining = frame.DataLength;
                if (remaining > 0)
                {
                    var totalRead = 0;
                    while (totalRead < remaining)
                    {
                        read = await _stream.ReadAsync(buffer, totalRead, remaining - totalRead, ct);
                        if (read <= 0) break;
                        totalRead += read;
                    }
                    if (totalRead < remaining) break;
                    frame.Data = buffer[..totalRead].ToArray();
                }

                var latency = _listener.GetLatencyMs();
                if (latency > 0) await Task.Delay(latency, ct);

                var response = _handler.HandleRequest(frame);
                if (response != null)
                    await _stream.WriteAsync(response, ct);
            }
        }
        catch { }
        finally
        {
            _listener.RemoveSession(this);
            Dispose();
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}
