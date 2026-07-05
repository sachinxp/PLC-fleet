using System;
using System.Collections.Generic;

namespace PLC.Protocols.S7;

public class S7Pdu
{
    public const int HeaderSize = 8;
    public const byte ProtocolId = 0x32;

    public const byte RosctrJob = 0x01;
    public const byte RosctrAck = 0x02;
    public const byte RosctrAckData = 0x03;

    public byte Protocol { get; set; }
    public byte Rosctr { get; set; }
    public ushort Redundancy { get; set; }
    public ushort PduRef { get; set; }
    public ushort ParamLen { get; set; }
    public ushort DataLen { get; set; }
    public byte[] Params { get; set; } = Array.Empty<byte>();
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public static S7Pdu? Parse(byte[] buffer)
    {
        if (buffer.Length < HeaderSize) return null;
        if (buffer[0] != ProtocolId) return null;

        var pdu = new S7Pdu
        {
            Protocol = buffer[0],
            Rosctr = buffer[1],
            Redundancy = (ushort)((buffer[2] << 8) | buffer[3]),
            PduRef = (ushort)((buffer[4] << 8) | buffer[5]),
            ParamLen = (ushort)((buffer[6] << 8) | buffer[7]),
            DataLen = (ushort)((buffer[8] << 8) | buffer[9])
        };

        int offset = 10;
        if (pdu.ParamLen > 0 && offset + pdu.ParamLen <= buffer.Length)
        {
            pdu.Params = buffer[offset..(offset + pdu.ParamLen)];
            offset += pdu.ParamLen;
        }
        if (pdu.DataLen > 0 && offset + pdu.DataLen <= buffer.Length)
        {
            pdu.Data = buffer[offset..(offset + pdu.DataLen)];
        }
        return pdu;
    }

    public byte[] Serialize()
    {
        ParamLen = (ushort)Params.Length;
        DataLen = (ushort)Data.Length;
        var buf = new byte[10 + ParamLen + DataLen];
        buf[0] = Protocol;
        buf[1] = Rosctr;
        buf[2] = (byte)(Redundancy >> 8); buf[3] = (byte)Redundancy;
        buf[4] = (byte)(PduRef >> 8); buf[5] = (byte)PduRef;
        buf[6] = (byte)(ParamLen >> 8); buf[7] = (byte)ParamLen;
        buf[8] = (byte)(DataLen >> 8); buf[9] = (byte)DataLen;
        if (ParamLen > 0) Params.CopyTo(buf, 10);
        if (DataLen > 0) Data.CopyTo(buf, 10 + ParamLen);
        return buf;
    }

    public static byte[] BuildReadVarParams(int itemCount, IEnumerable<S7Address> addresses)
    {
        var items = new List<byte>();
        items.Add((byte)itemCount);
        foreach (var addr in addresses)
        {
            items.Add(0x12);
            items.Add(0x0A);
            items.Add(0x10);
            items.Add((byte)(addr.SizeBits == 1 ? 0x01 : 0x02));
            items.Add((byte)(addr.SizeBits / 8 > 0 ? addr.SizeBits / 8 : 1));
            items.Add((byte)(addr.DbNumber >> 8));
            items.Add((byte)addr.DbNumber);
            items.Add((byte)addr.Area);
            items.Add((byte)(addr.ByteOffset >> 8));
            items.Add((byte)addr.ByteOffset);
            items.Add(0x00);
            if (addr.SizeBits == 1)
            {
                items.Add(addr.BitOffset);
            }
            else
            {
                items.Add(0x00);
            }
        }
        return items.ToArray();
    }

    public static byte[] BuildWriteVarParams(int itemCount, IEnumerable<S7Address> addresses)
    {
        var items = new List<byte>();
        items.Add((byte)itemCount);
        foreach (var addr in addresses)
        {
            items.Add(0x12);
            items.Add(0x0A);
            items.Add(0x10);
            items.Add((byte)(addr.SizeBits == 1 ? 0x01 : 0x02));
            items.Add((byte)(addr.SizeBits / 8 > 0 ? addr.SizeBits / 8 : 1));
            items.Add((byte)(addr.DbNumber >> 8));
            items.Add((byte)addr.DbNumber);
            items.Add((byte)addr.Area);
            items.Add((byte)(addr.ByteOffset >> 8));
            items.Add((byte)addr.ByteOffset);
            items.Add(0x00);
            items.Add(addr.SizeBits == 1 ? addr.BitOffset : (byte)0x00);
        }
        return items.ToArray();
    }

    public static byte[] BuildReadSzlParams(ushort szlId, ushort szlIndex = 0x0000)
    {
        return new byte[]
        {
            0x1D,
            0x00,
            (byte)(szlId >> 8),
            (byte)szlId,
            (byte)(szlIndex >> 8),
            (byte)szlIndex
        };
    }
}

public static class S7FunctionCode
{
    public const byte ReadVar = 0x04;
    public const byte WriteVar = 0x05;
    public const byte RequestDie = 0x1C;
    public const byte ReadSzl = 0x1D;
    public const byte EnDisableSubscription = 0x1E;
    public const byte ReadWriteVar = 0x1F;
    public const byte SetupCommunication = 0xF0;
}

public static class S7ReturnCode
{
    public const byte Reserved = 0x00;
    public const byte HardwareFault = 0x01;
    public const byte AccessingObjectNotAllowed = 0x03;
    public const byte AddressOutOfRange = 0x05;
    public const byte DataTypeNotSupported = 0x06;
    public const byte DataTypeInconsistent = 0x07;
    public const byte ObjectNotFound = 0x0A;
    public const byte Success = 0xFF;
}
