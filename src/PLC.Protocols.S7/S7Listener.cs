using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.S7;

public class S7Listener : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<S7Session> _sessions = new();
    private readonly int _maxConnections;
    private readonly int _baseLatencyMs;
    private readonly int _jitterMs;
    private S7RequestHandler _handler = null!;
    private readonly Random _rng = new();

    public S7Listener(int maxConnections = 8, int baseLatencyMs = 5, int jitterMs = 2)
    {
        _maxConnections = maxConnections;
        _baseLatencyMs = baseLatencyMs;
        _jitterMs = jitterMs;
    }

    public void Initialize(PlcInstance plc)
    {
        var szlProvider = new SzlProvider(plc);
        _handler = new S7RequestHandler(plc.Tags, szlProvider);
    }

    public S7RequestHandler Handler => _handler;

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
                var session = new S7Session(client, this, _handler);
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

    public void RemoveSession(S7Session session)
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
        lock (_sessions)
        {
            foreach (var s in _sessions) s.Dispose();
            _sessions.Clear();
        }
    }

    public void Dispose() { Stop(); _cts?.Dispose(); }
}

public class S7Session : IDisposable
{
    private readonly TcpClient _client;
    private readonly S7Listener _listener;
    private readonly S7RequestHandler _handler;
    private readonly NetworkStream _stream;
    private bool _cotpEstablished;

    public S7Session(TcpClient client, S7Listener listener, S7RequestHandler handler)
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
                var read = await _stream.ReadAsync(buffer, 0, 4, ct);
                if (read < 4) break;

                if (!CotpLayer.TryParseTpkt(buffer[..4], out var tpktLength, out var payload))
                    break;

                var remaining = tpktLength - 4;
                var totalRead = 0;
                while (totalRead < remaining)
                {
                    read = await _stream.ReadAsync(buffer, totalRead, remaining - totalRead, ct);
                    if (read <= 0) break;
                    totalRead += read;
                }
                if (totalRead < remaining) break;

                var fullPayload = payload.Concat(buffer[..totalRead]).ToArray();

                if (!_cotpEstablished)
                {
                    if (CotpLayer.IsConnectionRequest(fullPayload))
                    {
                        var cc = CotpLayer.BuildConnectionConfirm(0x01, 0x02);
                        var tpktCc = CotpLayer.MakeTpkt(cc);
                        await _stream.WriteAsync(tpktCc, ct);
                        _cotpEstablished = true;
                    }
                    continue;
                }

                if (CotpLayer.IsDataTransfer(fullPayload))
                {
                    var s7pduStart = 3;
                    if (fullPayload.Length <= s7pduStart) continue;

                    var latency = _listener.GetLatencyMs();
                    if (latency > 0) await Task.Delay(latency, ct);

                    var response = _handler.HandleRequest(fullPayload[s7pduStart..]);
                    if (response != null)
                    {
                        var dtResponse = CotpLayer.BuildDataTransfer(response);
                        var tpktResponse = CotpLayer.MakeTpkt(dtResponse);
                        await _stream.WriteAsync(tpktResponse, ct);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception) { }
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
