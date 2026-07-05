using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PLC.Protocols.Modbus;

public class ModbusSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly ModbusListener _listener;
    private readonly ModbusRequestHandler _handler;
    private readonly NetworkStream _stream;
    private ushort _requestCount;

    public ModbusSession(TcpClient client, ModbusListener listener, ModbusRequestHandler handler)
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
            var buffer = new byte[1024];
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                var read = await _stream.ReadAsync(buffer, 0, 4, ct);
                if (read < 4) break;

                var transactionId = (ushort)((buffer[0] << 8) | buffer[1]);
                var protocolId = (ushort)((buffer[2] << 8) | buffer[3]);
                if (protocolId != 0) break;

                read = await _stream.ReadAsync(buffer, 0, 2, ct);
                if (read < 2) break;
                var length = (ushort)((buffer[0] << 8) | buffer[1]);

                var remaining = length;
                var totalRead = 0;
                while (totalRead < remaining)
                {
                    read = await _stream.ReadAsync(buffer, totalRead, remaining - totalRead, ct);
                    if (read <= 0) break;
                    totalRead += read;
                }
                if (totalRead < remaining) break;

                var frameBytes = new byte[7 + remaining];
                frameBytes[0] = (byte)(transactionId >> 8);
                frameBytes[1] = (byte)transactionId;
                frameBytes[2] = (byte)(protocolId >> 8);
                frameBytes[3] = (byte)protocolId;
                frameBytes[4] = (byte)(length >> 8);
                frameBytes[5] = (byte)length;
                buffer[..remaining].CopyTo(frameBytes, 6);

                var frame = ModbusFrame.Parse(frameBytes);
                if (frame == null) break;

                _requestCount++;

                var latency = _listener.GetLatencyMs();
                if (latency > 0) await Task.Delay(latency, ct);

                byte[]? response;
                byte? exception;

                lock (_handler)
                {
                    (response, exception) = _handler.HandleRequest(
                        frame.FunctionCode, frame.Data, frame.TransactionId, frame.UnitId);
                }

                if (response != null)
                {
                    await _stream.WriteAsync(response, 0, response.Length, ct);
                }
                else if (exception.HasValue)
                {
                    var errResp = ModbusFrame.BuildErrorResponse(
                        frame.TransactionId, frame.UnitId, frame.FunctionCode, exception.Value);
                    await _stream.WriteAsync(errResp, 0, errResp.Length, ct);
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
