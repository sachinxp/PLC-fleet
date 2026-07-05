using System;
using System.Globalization;

namespace PLC.Protocols.S7;

public enum S7Area : byte
{
    Input = 0x81,
    Output = 0x82,
    Merker = 0x83,
    DataBlock = 0x84,
    Timer = 0x1D,
    Counter = 0x1C,
    SystemInfo = 0x06
}

public class S7Address
{
    public S7Area Area { get; set; }
    public int DbNumber { get; set; }
    public int ByteOffset { get; set; }
    public byte BitOffset { get; set; }
    public int SizeBits { get; set; }

    public S7Address(S7Area area, int byteOffset, int sizeBits, int dbNumber = 0, byte bitOffset = 0)
    {
        Area = area;
        DbNumber = dbNumber;
        ByteOffset = byteOffset;
        BitOffset = bitOffset;
        SizeBits = sizeBits;
    }

    public override string ToString() => Area switch
    {
        S7Area.Input => BitOffset > 0 ? $"I{ByteOffset}.{BitOffset}" : SizeBits switch { 1 => $"I{ByteOffset}.0", 8 => $"IB{ByteOffset}", 16 => $"IW{ByteOffset}", 32 => $"ID{ByteOffset}", _ => $"I{ByteOffset}" },
        S7Area.Output => BitOffset > 0 ? $"Q{ByteOffset}.{BitOffset}" : SizeBits switch { 1 => $"Q{ByteOffset}.0", 8 => $"QB{ByteOffset}", 16 => $"QW{ByteOffset}", 32 => $"QD{ByteOffset}", _ => $"Q{ByteOffset}" },
        S7Area.Merker => BitOffset > 0 ? $"M{ByteOffset}.{BitOffset}" : SizeBits switch { 1 => $"M{ByteOffset}.0", 8 => $"MB{ByteOffset}", 16 => $"MW{ByteOffset}", 32 => $"MD{ByteOffset}", _ => $"M{ByteOffset}" },
        S7Area.DataBlock => BitOffset > 0 ? $"DB{DbNumber}.DBX{ByteOffset}.{BitOffset}" : SizeBits switch { 1 => $"DB{DbNumber}.DBX{ByteOffset}.0", 8 => $"DB{DbNumber}.DBB{ByteOffset}", 16 => $"DB{DbNumber}.DBW{ByteOffset}", 32 => $"DB{DbNumber}.DBD{ByteOffset}", _ => $"DB{DbNumber}.{ByteOffset}" },
        S7Area.Timer => $"T{ByteOffset}",
        S7Area.Counter => $"C{ByteOffset}",
        _ => $"Area=0x{(byte)Area:X2},DB={DbNumber},Byte={ByteOffset},Bit={BitOffset},Size={SizeBits}"
    };
}

public static class S7AddressParser
{
    public static S7Address? Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        address = address.Trim().ToUpperInvariant();

        try
        {
            if (address.StartsWith("DB"))
            {
                var rest = address[2..];
                var dotIdx = rest.IndexOf('.');
                if (dotIdx < 0) return null;
                if (!int.TryParse(rest[..dotIdx], out var dbNum)) return null;
                var field = rest[(dotIdx + 1)..];

                if (field.StartsWith("DBX"))
                {
                    var coord = field[3..];
                    var bitDot = coord.IndexOf('.');
                    if (bitDot < 0) return new S7Address(S7Area.DataBlock, int.Parse(coord), 1, dbNum, 0);
                    return new S7Address(S7Area.DataBlock, int.Parse(coord[..bitDot]), 1, dbNum, byte.Parse(coord[(bitDot + 1)..]));
                }
                if (field.StartsWith("DBB"))
                    return new S7Address(S7Area.DataBlock, int.Parse(field[3..]), 8, dbNum);
                if (field.StartsWith("DBW"))
                    return new S7Address(S7Area.DataBlock, int.Parse(field[3..]), 16, dbNum);
                if (field.StartsWith("DBD"))
                    return new S7Address(S7Area.DataBlock, int.Parse(field[3..]), 32, dbNum);

                var valDot = field.IndexOf('.');
                if (valDot >= 0)
                    return new S7Address(S7Area.DataBlock, int.Parse(field[..valDot]), 1, dbNum, byte.Parse(field[(valDot + 1)..]));
                return new S7Address(S7Area.DataBlock, int.Parse(field), 8, dbNum);
            }

            if (address.StartsWith("ID"))
                return new S7Address(S7Area.Input, int.Parse(address[2..]), 32);
            if (address.StartsWith("IW"))
                return new S7Address(S7Area.Input, int.Parse(address[2..]), 16);
            if (address.StartsWith("IB"))
                return new S7Address(S7Area.Input, int.Parse(address[2..]), 8);
            if (address.StartsWith("I"))
            {
                var coord = address[1..];
                var dot = coord.IndexOf('.');
                if (dot >= 0) return new S7Address(S7Area.Input, int.Parse(coord[..dot]), 1, 0, byte.Parse(coord[(dot + 1)..]));
                return new S7Address(S7Area.Input, int.Parse(coord), 8);
            }

            if (address.StartsWith("QD"))
                return new S7Address(S7Area.Output, int.Parse(address[2..]), 32);
            if (address.StartsWith("QW"))
                return new S7Address(S7Area.Output, int.Parse(address[2..]), 16);
            if (address.StartsWith("QB"))
                return new S7Address(S7Area.Output, int.Parse(address[2..]), 8);
            if (address.StartsWith("Q"))
            {
                var coord = address[1..];
                var dot = coord.IndexOf('.');
                if (dot >= 0) return new S7Address(S7Area.Output, int.Parse(coord[..dot]), 1, 0, byte.Parse(coord[(dot + 1)..]));
                return new S7Address(S7Area.Output, int.Parse(coord), 8);
            }

            if (address.StartsWith("MD"))
                return new S7Address(S7Area.Merker, int.Parse(address[2..]), 32);
            if (address.StartsWith("MW"))
                return new S7Address(S7Area.Merker, int.Parse(address[2..]), 16);
            if (address.StartsWith("MB"))
                return new S7Address(S7Area.Merker, int.Parse(address[2..]), 8);
            if (address.StartsWith("M"))
            {
                var coord = address[1..];
                var dot = coord.IndexOf('.');
                if (dot >= 0) return new S7Address(S7Area.Merker, int.Parse(coord[..dot]), 1, 0, byte.Parse(coord[(dot + 1)..]));
                return new S7Address(S7Area.Merker, int.Parse(coord), 8);
            }

            if (address.StartsWith("T"))
                return new S7Address(S7Area.Timer, int.Parse(address[1..]), 16);

            if (address.StartsWith("C"))
                return new S7Address(S7Area.Counter, int.Parse(address[1..]), 16);
        }
        catch { return null; }

        return null;
    }

    public static string GetS7DataType(string address)
    {
        var a = address.Trim().ToUpperInvariant();
        if (a.Contains(".")) return "Bool";
        if (a.StartsWith("ID") || a.StartsWith("QD") || a.StartsWith("MD") || a.StartsWith("DBD")) return "Int32";
        if (a.StartsWith("IW") || a.StartsWith("QW") || a.StartsWith("MW") || a.StartsWith("DBW")) return "Int16";
        if (a.StartsWith("IB") || a.StartsWith("QB") || a.StartsWith("MB") || a.StartsWith("DBB") || a.StartsWith("DB")) return "Byte";
        if (a.StartsWith("T") || a.StartsWith("C")) return "Int16";
        if (a.StartsWith("I") || a.StartsWith("Q") || a.StartsWith("M")) return "Bool";
        return "Int16";
    }
}
