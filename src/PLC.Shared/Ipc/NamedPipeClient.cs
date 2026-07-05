using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PLC.Shared.Ipc;

public class NamedPipeClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly string _pipeName;

    public NamedPipeClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task ConnectAsync(int timeoutMs = 5000)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
        _pipe.Connect(timeoutMs);
        _reader = new StreamReader(_pipe, Encoding.UTF8);
        _writer = new StreamWriter(_pipe, Encoding.UTF8);
    }

    public async Task SendAsync(IpcMessage message)
    {
        if (_writer == null) throw new InvalidOperationException("Not connected");
        var json = JsonSerializer.Serialize(message);
        await _writer.WriteLineAsync(json);
        await _writer.FlushAsync();
    }

    public async Task<IpcMessage?> ReceiveAsync()
    {
        if (_reader == null) return null;
        var line = await _reader.ReadLineAsync();
        if (line == null) return null;
        return JsonSerializer.Deserialize<IpcMessage>(line);
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
    }
}
