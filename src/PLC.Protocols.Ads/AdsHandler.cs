using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PLC.Shared.Models;

namespace PLC.Protocols.Ads;

public class AdsHandler
{
    private readonly Dictionary<string, object?> _tagValues = new();
    private readonly List<TagDefinition> _tags;
    private readonly string _deviceName;
    private readonly string _version;
    private uint _invokeId;

    public AdsHandler(List<TagDefinition> tags, string deviceName = "TwinCAT 3 PLC", string version = "3.1.4024.56")
    {
        _tags = tags;
        _deviceName = deviceName;
        _version = version;
    }

    public void UpdateTagValue(string name, object? value)
    {
        lock (_tagValues) { _tagValues[name] = value; }
    }

    public byte[]? HandleRequest(AmsPacket request)
    {
        _invokeId = request.InvokeId;
        return request.CommandId switch
        {
            AmsPacket.ReadDeviceInfo => HandleReadDeviceInfo(request),
            AmsPacket.Read => HandleRead(request),
            AmsPacket.Write => HandleWrite(request),
            AmsPacket.ReadWrite => HandleReadWrite(request),
            AmsPacket.ReadState => HandleReadState(request),
            AmsPacket.AddNotification => HandleAddNotification(request),
            AmsPacket.DeleteNotification => HandleDeleteNotification(request),
            _ => AmsPacket.BuildErrorResponse(request, AmsPacket.ErrServiceNotSupported)
        };
    }

    private byte[] HandleReadDeviceInfo(AmsPacket request)
    {
        var nameBytes = Encoding.ASCII.GetBytes(_deviceName.PadRight(16, '\0')[..16]);
        var verParts = _version.Split('.');
        var major = verParts.Length > 0 ? ushort.Parse(verParts[0]) : (ushort)3;
        var minor = verParts.Length > 1 ? ushort.Parse(verParts[1]) : (ushort)1;
        var build = verParts.Length > 2 ? ushort.Parse(verParts[2]) : (ushort)4024;
        var data = new List<byte>();
        data.AddRange(nameBytes);
        data.Add((byte)major); data.Add((byte)(major >> 8));
        data.Add((byte)minor); data.Add((byte)(minor >> 8));
        data.Add((byte)build); data.Add((byte)(build >> 8));
        data.Add(0x00); data.Add(0x00); // spare
        return AmsPacket.BuildResponse(request, data.ToArray());
    }

    private byte[] HandleRead(AmsPacket request)
    {
        if (request.Data.Length < 8) return AmsPacket.BuildErrorResponse(request, AmsPacket.ErrClientPort);
        var indexGroup = BinaryPrimitives.ReadUInt32LittleEndian(request.Data[0..4]);
        var indexOffset = BinaryPrimitives.ReadUInt32LittleEndian(request.Data[4..8]);
        var readLen = request.Data.Length > 8 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(request.Data[8..12]) : 4;

        // Symbol handle: indexGroup=0xF003, indexOffset=handle value
        if (indexGroup == 0xF003)
        {
            var handle = indexOffset;
            var tag = _tags.FirstOrDefault(t => GetTagHandle(t.Name) == handle);
            if (tag == null) return AmsPacket.BuildErrorResponse(request, AmsPacket.ErrSymbolNotFound);

            object? value;
            lock (_tagValues) { _tagValues.TryGetValue(tag.Name, out value); }
            value ??= tag.Simulation.Value;

            var valBytes = ValueToBytes(value, readLen);
            var data = new byte[readLen + 4];
            BinaryPrimitives.WriteUInt32LittleEndian(data[0..4], (uint)readLen);
            valBytes.CopyTo(data, 4);
            return AmsPacket.BuildResponse(request, data);
        }

        return AmsPacket.BuildErrorResponse(request, AmsPacket.ErrClientPort);
    }

    private byte[] HandleWrite(AmsPacket request)
    {
        if (request.Data.Length < 12) return AmsPacket.BuildErrorResponse(request, AmsPacket.ErrClientPort);
        var indexGroup = BinaryPrimitives.ReadUInt32LittleEndian(request.Data[0..4]);
        var indexOffset = BinaryPrimitives.ReadUInt32LittleEndian(request.Data[4..8]);
        var writeLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(request.Data[8..12]);

        if (indexGroup == 0xF003)
        {
            var handle = indexOffset;
            var tag = _tags.FirstOrDefault(t => GetTagHandle(t.Name) == handle);
            if (tag == null) return AmsPacket.BuildErrorResponse(request, AmsPacket.ErrSymbolNotFound);

            var writeData = request.Data[12..];
            lock (_tagValues) { _tagValues[tag.Name] = writeData; }
        }

        return AmsPacket.BuildResponse(request, Array.Empty<byte>());
    }

    private byte[] HandleReadWrite(AmsPacket request)
    {
        // Simplified: read supported, write portion handled
        return HandleRead(request);
    }

    private byte[] HandleReadState(AmsPacket request)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(data[0..2], 5); // ADS state: Run
        BinaryPrimitives.WriteUInt16LittleEndian(data[2..4], 0); // Device state: OK
        return AmsPacket.BuildResponse(request, data);
    }

    private byte[] HandleAddNotification(AmsPacket request)
    {
        // Acknowledge notification subscription
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data[0..4], _invokeId);
        return AmsPacket.BuildResponse(request, data);
    }

    private byte[] HandleDeleteNotification(AmsPacket request)
    {
        return AmsPacket.BuildResponse(request, Array.Empty<byte>());
    }

    private uint GetTagHandle(string name)
    {
        // Deterministic handle from name hash
        uint hash = 0;
        foreach (var c in name) hash = (hash * 31) + c;
        return hash & 0x7FFFFFFF;
    }

    private byte[] ValueToBytes(object? value, int len)
    {
        var bytes = new byte[Math.Max(len, 1)];
        if (value == null) return bytes;

        if (value is bool b) bytes[0] = b ? (byte)1 : (byte)0;
        else if (value is short s) BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)s);
        else if (value is int i) BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)i);
        else if (value is uint u) BinaryPrimitives.WriteUInt32LittleEndian(bytes, u);
        else if (value is float f) BitConverter.GetBytes(f).CopyTo(bytes, 0);
        else if (value is double d) BitConverter.GetBytes(d).CopyTo(bytes, 0);
        else
        {
            var str = value.ToString() ?? "";
            Encoding.ASCII.GetBytes(str.PadRight(len, ' ')[..Math.Min(len, str.Length)]).CopyTo(bytes, 0);
        }
        return bytes;
    }
}
