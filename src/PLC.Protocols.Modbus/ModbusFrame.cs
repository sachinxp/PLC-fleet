using System;
using System.Buffers.Binary;

namespace PLC.Protocols.Modbus;

public class ModbusFrame
{
    public ushort TransactionId { get; set; }
    public ushort ProtocolId { get; set; } = 0;
    public ushort Length { get; set; }
    public byte UnitId { get; set; }
    public byte FunctionCode { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public const int MbapHeaderSize = 7; // 2+2+2+1

    public static ModbusFrame? Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < MbapHeaderSize + 1) return null;
        var frame = new ModbusFrame
        {
            TransactionId = BinaryPrimitives.ReadUInt16BigEndian(buffer[0..2]),
            ProtocolId = BinaryPrimitives.ReadUInt16BigEndian(buffer[2..4]),
            Length = BinaryPrimitives.ReadUInt16BigEndian(buffer[4..6]),
            UnitId = buffer[6],
            FunctionCode = buffer[7]
        };
        var dataLen = frame.Length - 2; // subtract unitId + fc
        if (dataLen > 0)
        {
            frame.Data = buffer.Slice(8, dataLen).ToArray();
        }
        return frame;
    }

    public byte[] Serialize()
    {
        Length = (ushort)(2 + Data.Length); // unitId + fc + data
        var buffer = new byte[MbapHeaderSize + 1 + Data.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span[0..2], TransactionId);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..4], ProtocolId);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..6], Length);
        buffer[6] = UnitId;
        buffer[7] = FunctionCode;
        if (Data.Length > 0)
            Data.CopyTo(buffer, 8);
        return buffer;
    }

    public static byte[] BuildErrorResponse(ushort transactionId, byte unitId, byte functionCode, byte exceptionCode)
    {
        var frame = new ModbusFrame
        {
            TransactionId = transactionId,
            UnitId = unitId,
            FunctionCode = (byte)(functionCode | 0x80),
            Data = new[] { exceptionCode }
        };
        return frame.Serialize();
    }

    public static byte[] BuildResponse(ushort transactionId, byte unitId, byte functionCode, byte[] data)
    {
        var frame = new ModbusFrame
        {
            TransactionId = transactionId,
            UnitId = unitId,
            FunctionCode = functionCode,
            Data = data
        };
        return frame.Serialize();
    }
}

public static class ModbusExceptionCode
{
    public const byte IllegalFunction = 0x01;
    public const byte IllegalDataAddress = 0x02;
    public const byte IllegalDataValue = 0x03;
    public const byte ServerDeviceFailure = 0x04;
    public const byte Acknowledge = 0x05;
    public const byte ServerDeviceBusy = 0x06;
}
