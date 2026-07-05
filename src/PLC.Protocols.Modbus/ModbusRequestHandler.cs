using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using PLC.Shared.Models;

namespace PLC.Protocols.Modbus;

public class ModbusRequestHandler
{
    private readonly ModbusAddressMap _addressMap;

    // In-memory tag store — this should be shared with the worker's TagEngine
    private readonly Dictionary<string, TagValue> _tagValues = new();

    public ModbusRequestHandler(ModbusAddressMap addressMap)
    {
        _addressMap = addressMap;
    }

    public void UpdateTagValue(string name, object value)
    {
        _tagValues[name] = new TagValue { Name = name, Value = value };
    }

    public (byte[]? response, byte? exceptionCode) HandleRequest(byte fc, byte[] data, ushort transactionId, byte unitId)
    {
        return fc switch
        {
            0x01 => ReadCoils(data, transactionId, unitId),
            0x02 => ReadDiscreteInputs(data, transactionId, unitId),
            0x03 => ReadHoldingRegisters(data, transactionId, unitId),
            0x04 => ReadInputRegisters(data, transactionId, unitId),
            0x05 => WriteSingleCoil(data, transactionId, unitId),
            0x06 => WriteSingleRegister(data, transactionId, unitId),
            0x0F => WriteMultipleCoils(data, transactionId, unitId),
            0x10 => WriteMultipleRegisters(data, transactionId, unitId),
            0x16 => MaskWriteRegister(data, transactionId, unitId),
            0x17 => ReadWriteMultipleRegisters(data, transactionId, unitId),
            0x2B => ReadDeviceIdentification(data, transactionId, unitId),
            _ => (null, ModbusExceptionCode.IllegalFunction)
        };
    }

    private (byte[]?, byte?) ReadCoils(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 4) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var startAddr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        if (quantity < 1 || quantity > 2000) return (null, ModbusExceptionCode.IllegalDataValue);

        var byteCount = (quantity + 7) / 8;
        var coilData = new byte[byteCount];
        for (int i = 0; i < quantity; i++)
        {
            var tag = _addressMap.GetCoil(startAddr + i);
            if (tag != null && _tagValues.TryGetValue(tag.Name, out var tv) && tv.Value is bool bVal && bVal)
                coilData[i / 8] |= (byte)(1 << (i % 8));
        }

        var resp = new byte[1 + byteCount];
        resp[0] = (byte)byteCount;
        coilData.CopyTo(resp, 1);
        return (ModbusFrame.BuildResponse(tid, uid, 0x01, resp), null);
    }

    private (byte[]?, byte?) ReadDiscreteInputs(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 4) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var startAddr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        if (quantity < 1 || quantity > 2000) return (null, ModbusExceptionCode.IllegalDataValue);

        var byteCount = (quantity + 7) / 8;
        var diData = new byte[byteCount];
        for (int i = 0; i < quantity; i++)
        {
            var tag = _addressMap.GetDiscreteInput(startAddr + i);
            if (tag != null && _tagValues.TryGetValue(tag.Name, out var tv) && tv.Value is bool bVal && bVal)
                diData[i / 8] |= (byte)(1 << (i % 8));
        }

        var resp = new byte[1 + byteCount];
        resp[0] = (byte)byteCount;
        diData.CopyTo(resp, 1);
        return (ModbusFrame.BuildResponse(tid, uid, 0x02, resp), null);
    }

    private (byte[]?, byte?) ReadHoldingRegisters(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 4) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var startAddr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        if (quantity < 1 || quantity > 125) return (null, ModbusExceptionCode.IllegalDataValue);

        var resp = new byte[1 + quantity * 2];
        resp[0] = (byte)(quantity * 2);
        for (int i = 0; i < quantity;)
        {
            var tag = _addressMap.GetHoldingRegister(startAddr + i);
            if (tag != null && _tagValues.TryGetValue(tag.Name, out var tv) && tv.Value != null)
            {
                var wordCount = ModbusAddressMap.WordCountForType(tag.DataType);
                if (i + wordCount <= quantity)
                {
                    WriteValueToRegisters(resp, 1 + i * 2, tv.Value, tag.DataType);
                    i += wordCount;
                    continue;
                }
            }
            i++;
        }
        return (ModbusFrame.BuildResponse(tid, uid, 0x03, resp), null);
    }

    private (byte[]?, byte?) ReadInputRegisters(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 4) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var startAddr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        if (quantity < 1 || quantity > 125) return (null, ModbusExceptionCode.IllegalDataValue);

        var resp = new byte[1 + quantity * 2];
        resp[0] = (byte)(quantity * 2);
        for (int i = 0; i < quantity;)
        {
            var tag = _addressMap.GetInputRegister(startAddr + i);
            if (tag != null && _tagValues.TryGetValue(tag.Name, out var tv) && tv.Value != null)
            {
                var wordCount = ModbusAddressMap.WordCountForType(tag.DataType);
                if (i + wordCount <= quantity)
                {
                    WriteValueToRegisters(resp, 1 + i * 2, tv.Value, tag.DataType);
                    i += wordCount;
                    continue;
                }
            }
            i++;
        }
        return (ModbusFrame.BuildResponse(tid, uid, 0x04, resp), null);
    }

    private (byte[]?, byte?) WriteSingleCoil(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 4) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var addr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var value = data[2] != 0 || data[3] != 0;
        _tagValues[$"coil_{addr}"] = new TagValue { Name = $"coil_{addr}", Value = value };
        return (ModbusFrame.BuildResponse(tid, uid, 0x05, dataSpan[..4].ToArray()), null);
    }

    private (byte[]?, byte?) WriteSingleRegister(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 4) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var addr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var value = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        _tagValues[$"hr_{addr}"] = new TagValue { Name = $"hr_{addr}", Value = (short)value };
        return (ModbusFrame.BuildResponse(tid, uid, 0x06, dataSpan[..4].ToArray()), null);
    }

    private (byte[]?, byte?) WriteMultipleCoils(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 5) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var startAddr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        if (quantity < 1 || quantity > 1968) return (null, ModbusExceptionCode.IllegalDataValue);
        var resp = new byte[4];
        dataSpan[..4].CopyTo(resp);
        return (ModbusFrame.BuildResponse(tid, uid, 0x0F, resp), null);
    }

    private (byte[]?, byte?) WriteMultipleRegisters(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 5) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var startAddr = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        if (quantity < 1 || quantity > 123) return (null, ModbusExceptionCode.IllegalDataValue);
        var resp = new byte[4];
        dataSpan[..4].CopyTo(resp);
        return (ModbusFrame.BuildResponse(tid, uid, 0x10, resp), null);
    }

    private (byte[]?, byte?) MaskWriteRegister(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 6) return (null, ModbusExceptionCode.IllegalDataAddress);
        return (ModbusFrame.BuildResponse(tid, uid, 0x16, data[..6]), null);
    }

    private (byte[]?, byte?) ReadWriteMultipleRegisters(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 9) return (null, ModbusExceptionCode.IllegalDataAddress);
        var dataSpan = data.AsSpan();
        var readStart = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[0..2]);
        var readQty = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[2..4]);
        var writeStart = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[4..6]);
        var writeQty = BinaryPrimitives.ReadUInt16BigEndian(dataSpan[6..8]);
        if (readQty < 1 || readQty > 125) return (null, ModbusExceptionCode.IllegalDataValue);

        var resp = new byte[1 + readQty * 2];
        resp[0] = (byte)(readQty * 2);
        for (int i = 0; i < readQty;)
        {
            var tag = _addressMap.GetHoldingRegister(readStart + i);
            if (tag != null && _tagValues.TryGetValue(tag.Name, out var tv) && tv.Value != null)
            {
                var wordCount = ModbusAddressMap.WordCountForType(tag.DataType);
                if (i + wordCount <= readQty)
                {
                    WriteValueToRegisters(resp, 1 + i * 2, tv.Value, tag.DataType);
                    i += wordCount;
                    continue;
                }
            }
            i++;
        }
        return (ModbusFrame.BuildResponse(tid, uid, 0x17, resp), null);
    }

    private (byte[]?, byte?) ReadDeviceIdentification(byte[] data, ushort tid, byte uid)
    {
        if (data.Length < 2) return (null, ModbusExceptionCode.IllegalDataAddress);
        var meiType = data[0];
        var readDevId = data[1];
        var resp = new List<byte> { meiType, readDevId, 0x01, 0x00, 0x03 };
        resp.Add(0x00); resp.Add(0x01); // Vendor ID
        var vendorBytes = System.Text.Encoding.ASCII.GetBytes("PLC Simulator");
        resp.Add((byte)vendorBytes.Length);
        resp.AddRange(vendorBytes);
        resp.Add(0x00); resp.Add(0x02);
        var prodBytes = System.Text.Encoding.ASCII.GetBytes("ModbusTCP-1.0");
        resp.Add((byte)prodBytes.Length);
        resp.AddRange(prodBytes);
        resp.Add(0x00); resp.Add(0x03);
        var revBytes = System.Text.Encoding.ASCII.GetBytes("1.0.0");
        resp.Add((byte)revBytes.Length);
        resp.AddRange(revBytes);

        var meiResp = new byte[resp.Count + 2];
        meiResp[0] = meiType;
        meiResp[1] = readDevId;
        resp.CopyTo(meiResp, 2);

        return (ModbusFrame.BuildResponse(tid, uid, 0x2B, meiResp), null);
    }

    private void WriteValueToRegisters(byte[] buffer, int offset, object value, string dataType)
    {
        var bufSpan = buffer.AsSpan();
        switch (dataType)
        {
            case "Bool":
                BinaryPrimitives.WriteUInt16BigEndian(bufSpan[offset..], (bool)value ? (ushort)0xFF00 : (ushort)0x0000);
                break;
            case "Int16":
                BinaryPrimitives.WriteInt16BigEndian(bufSpan[offset..], Convert.ToInt16(value));
                break;
            case "UInt16":
                BinaryPrimitives.WriteUInt16BigEndian(bufSpan[offset..], Convert.ToUInt16(value));
                break;
            case "Int32":
                BinaryPrimitives.WriteInt32BigEndian(bufSpan[offset..], Convert.ToInt32(value));
                break;
            case "UInt32":
                BinaryPrimitives.WriteUInt32BigEndian(bufSpan[offset..], Convert.ToUInt32(value));
                break;
            case "Float32":
                var floatBytes = BitConverter.GetBytes(Convert.ToSingle(value));
                if (BitConverter.IsLittleEndian) Array.Reverse(floatBytes);
                floatBytes.CopyTo(buffer, offset);
                break;
            case "Float64":
                var doubleBytes = BitConverter.GetBytes(Convert.ToDouble(value));
                if (BitConverter.IsLittleEndian) Array.Reverse(doubleBytes);
                doubleBytes.CopyTo(buffer, offset);
                break;
        }
    }
}

public class TagValue
{
    public string Name { get; set; } = "";
    public object? Value { get; set; }
}
