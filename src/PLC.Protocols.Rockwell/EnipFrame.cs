using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PLC.Protocols.Rockwell;

public class EnipFrame
{
    public ushort Command { get; set; }
    public ushort Length { get; set; }
    public uint SessionHandle { get; set; }
    public uint Status { get; set; }
    public ulong SenderContext { get; set; }
    public uint Options { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public const int HeaderSize = 24;

    // ENIP commands
    public const ushort ListIdentity = 0x0063;
    public const ushort ListServices = 0x0064;
    public const ushort RegisterSession = 0x0065;
    public const ushort UnregisterSession = 0x0066;
    public const ushort SendRRData = 0x006F;
    public const ushort SendUnitData = 0x0070;

    public static EnipFrame? Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize) return null;
        var frame = new EnipFrame
        {
            Command = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0..2]),
            Length = BinaryPrimitives.ReadUInt16LittleEndian(buffer[2..4]),
            SessionHandle = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..8]),
            Status = BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..12]),
            SenderContext = BinaryPrimitives.ReadUInt64LittleEndian(buffer[12..20]),
            Options = BinaryPrimitives.ReadUInt32LittleEndian(buffer[20..24])
        };
        if (frame.Length > 0 && HeaderSize + frame.Length <= buffer.Length)
            frame.Data = buffer.Slice(HeaderSize, frame.Length).ToArray();
        return frame;
    }

    public byte[] Serialize()
    {
        Length = (ushort)Data.Length;
        var buf = new byte[HeaderSize + Length];
        var s = buf.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s[0..2], Command);
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..4], Length);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..8], SessionHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..12], Status);
        BinaryPrimitives.WriteUInt64LittleEndian(s[12..20], SenderContext);
        BinaryPrimitives.WriteUInt32LittleEndian(s[20..24], Options);
        if (Length > 0) Data.CopyTo(buf, HeaderSize);
        return buf;
    }

    public static byte[] BuildSessionResponse(uint sessionHandle)
    {
        var frame = new EnipFrame
        {
            Command = RegisterSession,
            SessionHandle = sessionHandle,
            Status = 0,
            Data = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 }
        };
        return frame.Serialize();
    }

    public static byte[] BuildIdentityResponse()
    {
        var data = new List<byte>();
        // Encapsulation protocol version
        data.Add(0x01); data.Add(0x00);
        // Socket address (dummy)
        data.AddRange(new byte[16]);
        // Vendor ID (1 = Rockwell)
        data.Add(0x01); data.Add(0x00);
        // Device Type (14 = Controller)
        data.Add(0x0E); data.Add(0x00);
        // Product Code
        data.Add(0x01); data.Add(0x00);
        // Revision (major.minor)
        data.Add(0x01); data.Add(0x01);
        // Status
        data.Add(0x00); data.Add(0x00);
        // Serial number
        data.AddRange(BitConverter.GetBytes(12345678u));
        // Product name length
        data.Add(0x10);
        // Product name "PLC Simulator  "
        data.AddRange(System.Text.Encoding.ASCII.GetBytes("PLC Simulator  "));
        // State
        data.Add(0x00); data.Add(0x00);

        var frame = new EnipFrame
        {
            Command = ListIdentity,
            Status = 0,
            Data = data.ToArray()
        };
        return frame.Serialize();
    }
}
