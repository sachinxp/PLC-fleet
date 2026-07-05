using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using PLC.Shared.Models;

namespace PLC.Protocols.Melsec;

public class MelsecHandler
{
    private readonly Dictionary<string, object?> _tagValues = new();
    private readonly List<TagDefinition> _tags;
    private readonly string _modelName;

    public MelsecHandler(List<TagDefinition> tags, string modelName = "FX5U-32MT/ES")
    {
        _tags = tags;
        _modelName = modelName;
    }

    public void UpdateTagValue(string name, object? value)
    {
        lock (_tagValues) { _tagValues[name] = value; }
    }

    public byte[]? HandleRequest(McFrame request)
    {
        return (request.Command, request.Subcommand) switch
        {
            (0x0401, _) => HandleBatchReadBit(request),
            (0x0403, _) => HandleBatchReadWord(request),
            (0x1401, _) => HandleBatchWriteBit(request),
            (0x1403, _) => HandleBatchWriteWord(request),
            (0x0604, _) => HandleRandomRead(request),
            (0x1604, _) => HandleRandomWrite(request),
            (0x0001, 0x0000) => HandleModelNameRead(request),
            _ => McFrame.BuildErrorResponse(request.Command, request.Subcommand, 0xC051)
        };
    }

    private byte[]? HandleBatchReadBit(McFrame request)
    {
        if (request.Data.Length < 4) return McFrame.BuildErrorResponse(request.Command, request.Subcommand, 0xC05C);
        byte deviceCode = request.Data[0];
        var startAddr = BinaryPrimitives.ReadUInt16LittleEndian(request.Data[1..3]);
        var devPoints = request.Data[3];

        var data = new byte[devPoints];
        for (int i = 0; i < devPoints; i++)
        {
            var addr = DeviceAddrToString(deviceCode, startAddr + i);
            lock (_tagValues)
            {
                if (_tagValues.TryGetValue(addr, out var val) && val is bool b)
                    data[i] = b ? (byte)1 : (byte)0;
            }
        }
        var resp = McFrame.BuildResponse(McFrame.BatchReadBin, request.Command, request.Subcommand);
        // Data starts after standard header offset - need to append response data
        return AppendData(resp, data);
    }

    private byte[]? HandleBatchReadWord(McFrame request)
    {
        if (request.Data.Length < 4) return McFrame.BuildErrorResponse(request.Command, request.Subcommand, 0xC05C);
        byte deviceCode = request.Data[0];
        var startAddr = BinaryPrimitives.ReadUInt16LittleEndian(request.Data[1..3]);
        var devPoints = request.Data[3];

        var data = new byte[devPoints * 2];
        for (int i = 0; i < devPoints; i++)
        {
            var addr = DeviceAddrToString(deviceCode, startAddr + i);
            lock (_tagValues)
            {
                if (_tagValues.TryGetValue(addr, out var val))
                    WriteWordValue(data, i * 2, val);
            }
        }
        var resp = McFrame.BuildResponse(McFrame.BatchReadBin, request.Command, request.Subcommand);
        return AppendData(resp, data);
    }

    private byte[]? HandleBatchWriteBit(McFrame request)
    {
        if (request.Data.Length < 4) return McFrame.BuildErrorResponse(request.Command, request.Subcommand, 0xC05C);
        byte deviceCode = request.Data[0];
        var startAddr = BinaryPrimitives.ReadUInt16LittleEndian(request.Data[1..3]);
        var devPoints = request.Data[3];

        for (int i = 0; i < devPoints && 4 + i < request.Data.Length; i++)
        {
            var addr = DeviceAddrToString(deviceCode, startAddr + i);
            lock (_tagValues) { _tagValues[addr] = request.Data[4 + i] != 0; }
        }
        return McFrame.BuildResponse(McFrame.BatchWriteBin, request.Command, request.Subcommand);
    }

    private byte[]? HandleBatchWriteWord(McFrame request)
    {
        if (request.Data.Length < 4) return McFrame.BuildErrorResponse(request.Command, request.Subcommand, 0xC05C);
        byte deviceCode = request.Data[0];
        var startAddr = BinaryPrimitives.ReadUInt16LittleEndian(request.Data[1..3]);
        var devPoints = request.Data[3];

        for (int i = 0; i < devPoints; i++)
        {
            var addr = DeviceAddrToString(deviceCode, startAddr + i);
            if (4 + i * 2 + 1 < request.Data.Length)
            {
                var val = BinaryPrimitives.ReadUInt16LittleEndian(request.Data[(4 + i * 2)..]);
                lock (_tagValues) { _tagValues[addr] = (short)val; }
            }
        }
        return McFrame.BuildResponse(McFrame.BatchWriteBin, request.Command, request.Subcommand);
    }

    private byte[]? HandleRandomRead(McFrame request)
    {
        if (request.Data.Length < 4) return McFrame.BuildErrorResponse(request.Command, request.Subcommand, 0xC05C);
        var wordCount = BinaryPrimitives.ReadUInt16LittleEndian(request.Data[0..2]);
        var data = new List<byte>();

        int offset = 2;
        for (int i = 0; i < wordCount; i++)
        {
            if (offset + 3 > request.Data.Length) break;
            var deviceCode = request.Data[offset];
            var addr = BinaryPrimitives.ReadUInt16LittleEndian(request.Data[(offset + 1)..(offset + 3)]);
            offset += 3;
            var addrStr = DeviceAddrToString(deviceCode, addr);
            lock (_tagValues)
            {
                if (_tagValues.TryGetValue(addrStr, out var val))
                {
                    var bytes = new byte[2];
                    WriteWordValue(bytes, 0, val);
                    data.AddRange(bytes);
                }
                else data.AddRange(new byte[2]);
            }
        }

        var resp = McFrame.BuildResponse(McFrame.BatchReadBin, request.Command, request.Subcommand);
        return AppendData(resp, data.ToArray());
    }

    private byte[]? HandleRandomWrite(McFrame request)
    {
        // Stub - acknowledge
        return McFrame.BuildResponse(McFrame.BatchWriteBin, request.Command, request.Subcommand);
    }

    private byte[]? HandleModelNameRead(McFrame request)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(_modelName.PadRight(16, '\0')[..16]);
        var resp = McFrame.BuildResponse(McFrame.BatchReadBin, request.Command, request.Subcommand);
        return AppendData(resp, nameBytes);
    }

    private string DeviceAddrToString(byte deviceCode, int addr) => deviceCode switch
    {
        McFrame.DeviceM => $"M{addr}",
        McFrame.DeviceX => $"X{addr}",
        McFrame.DeviceY => $"Y{addr}",
        McFrame.DeviceB => $"B{addr}",
        McFrame.DeviceD => $"D{addr}",
        McFrame.DeviceW => $"W{addr}",
        McFrame.DeviceR => $"R{addr}",
        McFrame.DeviceZR => $"ZR{addr}",
        _ => $"D{addr}"
    };

    private void WriteWordValue(byte[] buffer, int offset, object? value)
    {
        if (value is short s) BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], (ushort)s);
        else if (value is int i) BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], (ushort)i);
        else if (value is float f) BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], (ushort)f);
        else if (value is ushort u) BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], u);
        else BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], 0);
    }

    private byte[] AppendData(byte[] response, byte[] data)
    {
        var combined = new byte[response.Length + data.Length];
        response.CopyTo(combined, 0);
        data.CopyTo(combined, response.Length);
        var s = combined.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s[8..10], (ushort)(data.Length));
        return combined;
    }
}
