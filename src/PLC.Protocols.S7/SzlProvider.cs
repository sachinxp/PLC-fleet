using System;
using System.Collections.Generic;
using PLC.Shared.Models;

namespace PLC.Protocols.S7;

public class SzlProvider
{
    private readonly PlcInstance _plc;

    public SzlProvider(PlcInstance plc) => _plc = plc;

    public byte[]? GetSzlData(ushort szlId)
    {
        return szlId switch
        {
            0x0011 => GetModuleId(),
            0x001C => GetCpuCharacteristics(),
            0x0001 => GetBootData(),
            0x011C => GetComponentId(),
            _ => null
        };
    }

    private byte[] GetModuleId()
    {
        var orderCode = _plc.OrderCode.PadRight(20, ' ')[..20];
        var bytes = new List<byte>();
        bytes.Add(0x11);
        bytes.Add(0x00);
        bytes.Add(0x00);
        bytes.Add(0x00);
        bytes.Add(0x01);
        bytes.Add(0x00);
        bytes.Add(0x22);
        bytes.Add(0x00);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(orderCode));
        while (bytes.Count < 26) bytes.Add(0x20);
        var fw = _plc.FirmwareVersion.Replace("V", "").Replace(".", "");
        for (int i = 0; i < 4 && i < fw.Length; i++)
            bytes.Add((byte)fw[i]);
        while (bytes.Count < 30) bytes.Add(0x00);
        var serial = _plc.SerialNumber.PadRight(24, ' ')[..24];
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(serial));
        return bytes.ToArray();
    }

    private byte[] GetCpuCharacteristics()
    {
        var bytes = new List<byte>
        {
            0x1C, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x10, 0x00
        };
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        return bytes.ToArray();
    }

    private byte[] GetBootData()
    {
        return new byte[]
        {
            0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x04, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
    }

    private byte[] GetComponentId()
    {
        var bytes = new List<byte>
        {
            0x1C, 0x01, 0x00, 0x00, 0x04, 0x00,
            0x44, 0x00
        };
        var entries = new[] {
            ("OrderCode", _plc.OrderCode),
            ("Serial", _plc.SerialNumber),
            ("Firmware", _plc.FirmwareVersion),
            ("Description", _plc.Description)
        };
        foreach (var (name, val) in entries)
        {
            var padded = val.PadRight(24, ' ')[..24];
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(padded));
        }
        return bytes.ToArray();
    }
}
