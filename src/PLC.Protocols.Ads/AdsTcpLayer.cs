using System;
using System.Buffers.Binary;

namespace PLC.Protocols.Ads;

public static class AdsTcpLayer
{
    // ADS over TCP: 4-byte length prefix + AMS packet
    public static byte[] Frame(byte[] amsPacket)
    {
        var frame = new byte[4 + amsPacket.Length];
        var s = frame.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s[0..4], (uint)amsPacket.Length);
        amsPacket.CopyTo(frame, 4);
        return frame;
    }

    public static bool TryParse(byte[] buffer, out int frameLength, out byte[] amsPacket)
    {
        frameLength = 0;
        amsPacket = Array.Empty<byte>();
        if (buffer.Length < 4) return false;
        var amsLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[0..4]);
        frameLength = 4 + amsLen;
        if (frameLength > buffer.Length) return false;
        amsPacket = buffer[4..frameLength];
        return true;
    }
}
