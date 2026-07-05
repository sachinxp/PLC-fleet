using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.Rockwell;

public class RockwellSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly RockwellListener _listener;
    private readonly uint _sessionHandle;
    private readonly NetworkStream _stream;
    private readonly Dictionary<string, object?> _tagValues;
    private readonly List<TagDefinition> _tags;


    public RockwellSession(TcpClient client, RockwellListener listener, uint handle, List<TagDefinition> tags, Dictionary<string, object?> tagValues)
    {
        _client = client;
        _listener = listener;
        _sessionHandle = handle;
        _tags = tags;
        _tagValues = tagValues;
        _stream = client.GetStream();
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                // Read ENIP header (24 bytes)
                var read = await _stream.ReadAsync(buffer, 0, EnipFrame.HeaderSize, ct);
                if (read < EnipFrame.HeaderSize) break;

                var frame = EnipFrame.Parse(buffer[..read]);
                if (frame == null) break;

                // Read remaining data
                if (frame.Length > 0)
                {
                    var remaining = frame.Length;
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

                // Simulate latency
                var latency = _listener.GetLatencyMs();
                if (latency > 0) await Task.Delay(latency, ct);

                byte[]? response = null;

                switch (frame.Command)
                {
                    case EnipFrame.RegisterSession:
                        response = EnipFrame.BuildSessionResponse(frame.SessionHandle > 0 ? frame.SessionHandle : _sessionHandle);
                        break;

                    case EnipFrame.UnregisterSession:
                        break;

                    case EnipFrame.SendRRData:
                    case EnipFrame.SendUnitData:
                        if (frame.Data.Length > 0)
                            response = HandleCipRequest(frame);
                        break;
                }

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

    private byte[]? HandleCipRequest(EnipFrame enipFrame)
    {
        var data = enipFrame.Data;
        if (data.Length < 2) return null;

        // Interface handle (4) + timeout (2) + item count (2) + items
        int offset = 0;
        if (data.Length < 8) return null;
        // Skip interface handle (4 bytes)
        offset += 4;
        // Timeout
        offset += 2;
        // Item count
        var itemCount = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]); offset += 2;

        // Find CIP data item (type 0x00B2 = connected data, type 0x00B1 = unconnected)
        byte[]? cipData = null;
        for (int i = 0; i < itemCount; i++)
        {
            if (offset + 4 > data.Length) break;
            var typeId = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]); offset += 2;
            var itemLen = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]); offset += 2;
            if (offset + itemLen > data.Length) break;
            if (typeId == 0x00B1 || typeId == 0x00B2)
            {
                cipData = data[offset..(offset + itemLen)];
                break;
            }
            offset += itemLen;
        }

        if (cipData == null || cipData.Length < 2) return null;

        var service = cipData[0];
        // Skip path
        var pathSize = (cipData[1] & 0x0F); // path size in words
        var cipOffset = 2 + pathSize * 2;
        if (cipOffset > cipData.Length) return null;

        var reqPayload = cipData[cipOffset..];

        byte[]? cipResponse = service switch
        {
            CipLayer.GetAttributesAll => HandleGetAttributesAll(reqPayload, service),
            CipLayer.ReadTag or CipLayer.ReadTagFragmented => HandleReadTag(reqPayload, service),
            CipLayer.WriteTag => HandleWriteTag(reqPayload, service),
            CipLayer.GetInstanceAttributesList => HandleBrowse(reqPayload, service),
            _ => CipLayer.BuildErrorResponse(service, CipLayer.ServiceNotSupported)
        };

        if (cipResponse == null) return null;

        // Build ENIP response with CIP data
        var responseItems = new List<byte>();
        // Sequence count item (type 0x8000, 2 bytes)
        responseItems.Add(0x00); responseItems.Add(0x80);
        responseItems.Add(0x02); responseItems.Add(0x00);
        responseItems.Add(0x00); responseItems.Add(0x00);
        // CIP data item (type 0x00B1)
        responseItems.Add(0xB1); responseItems.Add(0x00);
        var cipLen = (ushort)cipResponse.Length;
        responseItems.Add((byte)cipLen); responseItems.Add((byte)(cipLen >> 8));
        responseItems.AddRange(cipResponse);

        var respFrame = new EnipFrame
        {
            Command = enipFrame.Command,
            SessionHandle = _sessionHandle,
            Status = 0,
            Data = responseItems.ToArray()
        };
        return respFrame.Serialize();
    }

    private byte[] HandleGetAttributesAll(byte[] data, byte service)
    {
        // Return Identity object data
        var id = new List<byte>();
        id.AddRange(new byte[] { 0x01, 0x00 }); // Vendor (Rockwell = 1)
        id.AddRange(new byte[] { 0x0E, 0x00 }); // Device Type (Controller)
        id.AddRange(new byte[] { 0x01, 0x00 }); // Product Code
        id.AddRange(new byte[] { 0x01, 0x01 }); // Revision
        id.AddRange(new byte[] { 0x00, 0x00 }); // Status
        id.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // Serial
        id.Add(0x0C); // Product name length
        id.AddRange(System.Text.Encoding.ASCII.GetBytes("PLC_Simulator"));
        return CipLayer.BuildCipResponse(service, CipLayer.Success, id.ToArray());
    }

    private byte[] HandleReadTag(byte[] data, byte service)
    {
        var tagName = CipLayer.ParseTagName(data, out _);
        if (tagName == null)
            return CipLayer.BuildErrorResponse(service, CipLayer.InvalidParameter);

        object? value = null;
        lock (_tagValues) { _tagValues.TryGetValue(tagName, out value); }

        if (value == null)
        {
            // Try to find from tag definitions
            var tag = _tags.FirstOrDefault(t => t.Name == tagName);
            if (tag != null)
                value = tag.Simulation.Value;
            else
                return CipLayer.BuildErrorResponse(service, CipLayer.PathDestinationUnknown);
        }

        var valBytes = ValueToBytes(value);
        return CipLayer.BuildCipResponse(service, CipLayer.Success, valBytes);
    }

    private byte[] HandleWriteTag(byte[] data, byte service)
    {
        var tagName = CipLayer.ParseTagName(data, out var offset);
        if (tagName == null || offset >= data.Length)
            return CipLayer.BuildErrorResponse(service, CipLayer.InvalidParameter);

        var writeData = data[offset..];
        lock (_tagValues) { _tagValues[tagName] = writeData; }
        return CipLayer.BuildCipResponse(service, CipLayer.Success, Array.Empty<byte>());
    }

    private byte[] HandleBrowse(byte[] data, byte service)
    {
        var tagNames = _tags.Where(t => t.Enabled).Select(t => t.Name);
        return CipLayer.BuildBrowseResponse(tagNames, service);
    }

    private byte[] ValueToBytes(object? value)
    {
        if (value == null) return new byte[] { 0x00, 0x00, 0x00, 0x00 };
        if (value is bool b) return new byte[] { b ? (byte)1 : (byte)0, 0x00, 0x00, 0x00 };
        if (value is short s) { var buf = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(buf, s); return buf; }
        if (value is int i) { var buf = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(buf, i); return buf; }
        if (value is float f) return BitConverter.GetBytes(f);
        if (value is double d) return BitConverter.GetBytes(d);
        var str = value.ToString() ?? "";
        return System.Text.Encoding.ASCII.GetBytes(str.PadRight(4, ' ')[..4]);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}
