using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PLC.Protocols.Melsec;

public class McFrame
{
    // MC 3E frame header: 14 bytes (subheader + network + plc + io + station + timer)
    public const int HeaderSize = 14;

    // Subheaders
    public const ushort BatchReadBin = 0x5000;    // Batch read in binary
    public const ushort BatchWriteBin = 0x1400;   // Batch write in binary
    public const ushort RandomReadBin = 0x0403;   // Random read in binary
    public const ushort RandomWriteBin = 0x1403;  // Random write in binary

    public ushort Subheader { get; set; }
    public byte NetworkNo { get; set; }
    public byte PlcNo { get; set; }
    public ushort IoNo { get; set; }
    public ushort StationNo { get; set; }
    public ushort DataLength { get; set; }
    public ushort MonitoringTimer { get; set; } = 0x2710; // 10s
    public ushort Command { get; set; }
    public ushort Subcommand { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    // Device codes
    public const byte DeviceM = 0x4D;   // Internal relay
    public const byte DeviceX = 0x58;   // Input
    public const byte DeviceY = 0x59;   // Output
    public const byte DeviceB = 0x42;   // Link relay
    public const byte DeviceD = 0x44;   // Data register
    public const byte DeviceW = 0x57;   // Link register
    public const byte DeviceR = 0x52;   // File register
    public const byte DeviceZR = 0xEA;  // Extended file register

    public static McFrame? Parse(byte[] buffer)
    {
        if (buffer.Length < HeaderSize) return null;
        var frame = new McFrame
        {
            Subheader = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0..2]),
            NetworkNo = buffer[2],
            PlcNo = buffer[3],
            IoNo = BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..6]),
            StationNo = BinaryPrimitives.ReadUInt16LittleEndian(buffer[6..8]),
            DataLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer[8..10]),
            MonitoringTimer = BinaryPrimitives.ReadUInt16LittleEndian(buffer[10..12]),
            Command = BinaryPrimitives.ReadUInt16LittleEndian(buffer[12..14]),
            Subcommand = buffer.Length > 14 ? BinaryPrimitives.ReadUInt16LittleEndian(buffer[14..16]) : (ushort)0,
        };
        var dataStart = 16; // header + command + subcommand
        var dataLen = frame.DataLength;
        if (dataLen > 0 && dataStart + dataLen <= buffer.Length)
            frame.Data = buffer[dataStart..(dataStart + dataLen)];
        return frame;
    }

    public byte[] Serialize()
    {
        DataLength = (ushort)((Command != 0 ? 4 : 0) + Data.Length); // cmd + subcmd + data
        var buf = new byte[HeaderSize + (Command != 0 ? 4 : 0) + Data.Length];
        var s = buf.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s[0..2], Subheader);
        buf[2] = NetworkNo;
        buf[3] = PlcNo;
        BinaryPrimitives.WriteUInt16LittleEndian(s[4..6], IoNo);
        BinaryPrimitives.WriteUInt16LittleEndian(s[6..8], StationNo);
        BinaryPrimitives.WriteUInt16LittleEndian(s[8..10], DataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(s[10..12], MonitoringTimer);
        BinaryPrimitives.WriteUInt16LittleEndian(s[12..14], Command);
        BinaryPrimitives.WriteUInt16LittleEndian(s[14..16], Subcommand);
        if (Data.Length > 0) Data.CopyTo(buf, 16);
        return buf;
    }

    // Build success response
    public static byte[] BuildResponse(ushort subheader, ushort command, ushort subcommand)
    {
        var frame = new McFrame
        {
            Subheader = subheader,
            Command = command,
            Subcommand = subcommand,
        };
        return frame.Serialize();
    }

    // Build error response
    public static byte[] BuildErrorResponse(ushort command, ushort subcommand, ushort errorCode)
    {
        var frame = new McFrame
        {
            Subheader = BatchReadBin,
            Command = command,
            Subcommand = subcommand,
            Data = new byte[] { (byte)errorCode, (byte)(errorCode >> 8) }
        };
        return frame.Serialize();
    }

    public static bool IsBitDevice(byte deviceCode) => deviceCode switch
    {
        DeviceM or DeviceX or DeviceY or DeviceB => true,
        _ => false
    };

    public static int DeviceCodeToSize(byte deviceCode) => IsBitDevice(deviceCode) ? 1 : 2;
}
