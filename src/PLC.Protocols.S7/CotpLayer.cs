using System;

namespace PLC.Protocols.S7;

public class CotpLayer
{
    public const byte CotpConnectionRequest = 0xE0;
    public const byte CotpConnectionConfirm = 0xD0;
    public const byte CotpDataTransfer = 0xF0;
    public const byte CotpDisconnectRequest = 0x80;

    public static byte[] MakeTpkt(byte[] data)
    {
        var len = data.Length + 4;
        return new byte[] { 0x03, 0x00, (byte)(len >> 8), (byte)len }.Concat(data).ToArray();
    }

    public static bool TryParseTpkt(byte[] buffer, out int tpktLength, out byte[] payload)
    {
        tpktLength = 0;
        payload = Array.Empty<byte>();
        if (buffer.Length < 4) return false;
        if (buffer[0] != 0x03) return false;
        tpktLength = (buffer[2] << 8) | buffer[3];
        if (tpktLength < 4) return false;
        // payload will be filled by caller after reading remaining bytes
        return true;
    }

    public static byte[] BuildConnectionRequest(byte srcTsap, byte dstTsap)
    {
        var cr = new byte[]
        {
            0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
            0xC0, 0x01, 0x0A,
            0xC1, 0x02, srcTsap, 0x00,
            0xC2, 0x02, dstTsap, 0x00
        };
        return cr;
    }

    public static byte[] BuildConnectionConfirm(byte srcTsap, byte dstTsap)
    {
        var cc = new byte[]
        {
            0x11, 0xD0, 0x00, 0x00, 0x00, 0x01, 0x00,
            0xC0, 0x01, 0x0A,
            0xC1, 0x02, srcTsap, 0x00,
            0xC2, 0x02, dstTsap, 0x00
        };
        return cc;
    }

    public static byte[] BuildDataTransfer(byte[] s7pdu)
    {
        var dt = new byte[3 + s7pdu.Length];
        dt[0] = 0x02;
        dt[1] = 0xF0;
        dt[2] = 0x80;
        s7pdu.CopyTo(dt, 3);
        return dt;
    }

    public static bool IsConnectionRequest(byte[] cotpPayload) =>
        cotpPayload.Length > 1 && cotpPayload[1] == CotpConnectionRequest;

    public static bool IsDataTransfer(byte[] cotpPayload) =>
        cotpPayload.Length > 1 && cotpPayload[1] == CotpDataTransfer;
}
