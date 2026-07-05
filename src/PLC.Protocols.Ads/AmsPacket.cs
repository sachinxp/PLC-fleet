using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PLC.Protocols.Ads;

public class AmsPacket
{
    // AMS header: 32 bytes
    public const int HeaderSize = 32;

    public byte[] TargetNetId { get; set; } = new byte[6];
    public ushort TargetPort { get; set; }
    public byte[] SourceNetId { get; set; } = new byte[6];
    public ushort SourcePort { get; set; }
    public ushort CommandId { get; set; }
    public ushort StateFlags { get; set; }
    public uint DataLength { get; set; }
    public uint ErrorCode { get; set; }
    public uint InvokeId { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    // ADS commands
    public const ushort ReadDeviceInfo = 0x0001;
    public const ushort Read = 0x0002;
    public const ushort Write = 0x0003;
    public const ushort ReadWrite = 0x0005;
    public const ushort AddNotification = 0x0006;
    public const ushort DeleteNotification = 0x0007;
    public const ushort Notify = 0x0008;
    public const ushort ReadState = 0x000F;

    // Error codes
    public const uint ErrNoError = 0x00000000;
    public const uint ErrClientPort = 0x00000700;
    public const uint ErrSymbolNotFound = 0x00000710;
    public const uint ErrServiceNotSupported = 0x00000740;

    public static AmsPacket? Parse(byte[] buffer)
    {
        if (buffer.Length < HeaderSize) return null;
        var pkt = new AmsPacket();
        buffer[0..6].CopyTo(pkt.TargetNetId, 0);
        pkt.TargetPort = BinaryPrimitives.ReadUInt16LittleEndian(buffer[6..8]);
        buffer[8..14].CopyTo(pkt.SourceNetId, 0);
        pkt.SourcePort = BinaryPrimitives.ReadUInt16LittleEndian(buffer[14..16]);
        pkt.CommandId = BinaryPrimitives.ReadUInt16LittleEndian(buffer[16..18]);
        pkt.StateFlags = BinaryPrimitives.ReadUInt16LittleEndian(buffer[18..20]);
        pkt.DataLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer[20..24]);
        pkt.ErrorCode = BinaryPrimitives.ReadUInt32LittleEndian(buffer[24..28]);
        pkt.InvokeId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[28..32]);
        if (pkt.DataLength > 0 && HeaderSize + pkt.DataLength <= buffer.Length)
            pkt.Data = buffer[HeaderSize..(HeaderSize + (int)pkt.DataLength)];
        return pkt;
    }

    public byte[] Serialize()
    {
        DataLength = (uint)Data.Length;
        var buf = new byte[HeaderSize + Data.Length];
        TargetNetId.CopyTo(buf, 0);
        var s = buf.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s[6..8], TargetPort);
        SourceNetId.CopyTo(buf, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(s[14..16], SourcePort);
        BinaryPrimitives.WriteUInt16LittleEndian(s[16..18], CommandId);
        BinaryPrimitives.WriteUInt16LittleEndian(s[18..20], StateFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(s[20..24], DataLength);
        BinaryPrimitives.WriteUInt32LittleEndian(s[24..28], ErrorCode);
        BinaryPrimitives.WriteUInt32LittleEndian(s[28..32], InvokeId);
        if (Data.Length > 0) Data.CopyTo(buf, HeaderSize);
        return buf;
    }

    public static byte[] BuildResponse(AmsPacket request, byte[] responseData)
    {
        var resp = new AmsPacket
        {
            TargetNetId = request.SourceNetId,
            TargetPort = request.SourcePort,
            SourceNetId = request.TargetNetId,
            SourcePort = request.TargetPort,
            CommandId = request.CommandId,
            StateFlags = request.StateFlags,
            ErrorCode = 0,
            InvokeId = request.InvokeId,
            Data = responseData
        };
        return resp.Serialize();
    }

    public static byte[] BuildErrorResponse(AmsPacket request, uint errorCode)
    {
        var resp = new AmsPacket
        {
            TargetNetId = request.SourceNetId,
            TargetPort = request.SourcePort,
            SourceNetId = request.TargetNetId,
            SourcePort = request.TargetPort,
            CommandId = request.CommandId,
            StateFlags = request.StateFlags,
            ErrorCode = errorCode,
            InvokeId = request.InvokeId,
            Data = Array.Empty<byte>()
        };
        return resp.Serialize();
    }

    public static byte[] NetIdFromIp(string ip)
    {
        var parts = ip.Split('.');
        var netId = new byte[6];
        for (int i = 0; i < 4 && i < parts.Length; i++)
            byte.TryParse(parts[i], out netId[i]);
        netId[4] = 1; netId[5] = 1;
        return netId;
    }
}
