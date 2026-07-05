using System;
using System.Collections.Generic;
using System.Linq;
using PLC.Shared.Ipc;
using PLC.Shared.Models;

namespace PLC.Protocols.S7;

public class S7RequestHandler
{
    private readonly Dictionary<string, TagValue> _tagValues = new();
    private readonly List<TagDefinition> _tags;
    private readonly SzlProvider _szlProvider;
    private ushort _pduRef;

    public S7RequestHandler(List<TagDefinition> tags, SzlProvider szlProvider)
    {
        _tags = tags;
        _szlProvider = szlProvider;
    }

    public void UpdateTagValue(string name, object value)
    {
        _tagValues[name] = new TagValue { TagName = name, Value = value?.ToString() ?? "" };
    }

    public byte[]? HandleRequest(byte[] s7pdu)
    {
        var pdu = S7Pdu.Parse(s7pdu);
        if (pdu == null) return null;

        _pduRef = pdu.PduRef;

        if (pdu.Params.Length < 1) return null;

        // Setup Communication: ROSCTR=Job, param[0..1]=0x0000, param_len>=8, no data
        if (pdu.Rosctr == S7Pdu.RosctrJob && pdu.Params.Length >= 8 &&
            pdu.Params[0] == 0x00 && pdu.Params[1] == 0x00)
        {
            return HandleSetupCommunication(pdu);
        }

        var function = pdu.Params[0];
        return function switch
        {
            S7FunctionCode.ReadVar => HandleReadVar(pdu),
            S7FunctionCode.WriteVar => HandleWriteVar(pdu),
            S7FunctionCode.RequestDie => null,
            S7FunctionCode.ReadSzl => HandleReadSzl(pdu),
            S7FunctionCode.SetupCommunication => HandleSetupCommunication(pdu),
            _ => BuildErrorResponse(pdu, S7ReturnCode.ObjectNotFound)
        };
    }

    private byte[]? HandleReadVar(S7Pdu pdu)
    {
        if (pdu.Params.Length < 1) return BuildErrorResponse(pdu, S7ReturnCode.AddressOutOfRange);
        var count = pdu.Params[1];

        var dataItems = new List<byte>();

        for (int i = 0; i < count; i++)
        {
            int offset = 2 + i * 12;
            if (offset + 12 > pdu.Params.Length)
            {
                break;
            }

            var specType = pdu.Params[offset];
            var length = pdu.Params[offset + 1];
            var syntaxId = pdu.Params[offset + 2];
            var transportSize = pdu.Params[offset + 3];
            var dataLenBytes = pdu.Params[offset + 4];
            var dbHigh = pdu.Params[offset + 5];
            var dbLow = pdu.Params[offset + 6];
            var area = pdu.Params[offset + 7];
            var byteHigh = pdu.Params[offset + 8];
            var byteLow = pdu.Params[offset + 9];
            _ = pdu.Params[offset + 10];
            var bitOffset = pdu.Params[offset + 11];

            var dbNumber = (dbHigh << 8) | dbLow;
            var byteOffset = (byteHigh << 8) | byteLow;

            var sizeBits = transportSize == 1 ? 1 : dataLenBytes * 8;
            var s7Addr = new S7Address((S7Area)area, byteOffset, sizeBits, dbNumber,
                transportSize == 1 ? bitOffset : (byte)0);
            var tag = FindTag(s7Addr);

            if (tag != null && _tagValues.TryGetValue(tag.Name, out var tv) && tv.Value != null)
            {
                dataItems.Add(S7ReturnCode.Success);
                dataItems.Add(tag.DataType == "Bool" ? (byte)0x03 : (byte)0x04);
                var writeLen = dataLenBytes;
                dataItems.Add((byte)writeLen);
                var bytes = TagValueToBytes(tv.Value, tag.DataType, dataLenBytes);
                dataItems.AddRange(bytes);
            }
            else
            {
                dataItems.Add(S7ReturnCode.ObjectNotFound);
                dataItems.Add(0x00);
            }
        }

        var resp = new S7Pdu
        {
            Protocol = S7Pdu.ProtocolId,
            Rosctr = S7Pdu.RosctrAckData,
            PduRef = _pduRef,
            Params = new byte[] { S7FunctionCode.ReadVar, (byte)count, 0x00, 0x00 },
            Data = dataItems.ToArray()
        };
        return resp.Serialize();
    }

    private byte[]? HandleWriteVar(S7Pdu pdu)
    {
        if (pdu.Params.Length < 1) return BuildErrorResponse(pdu, S7ReturnCode.AddressOutOfRange);
        var count = pdu.Params[1];

        var resultItems = new List<byte>();
        for (int i = 0; i < count; i++)
        {
            resultItems.Add(S7ReturnCode.Success);
        }

        var resp = new S7Pdu
        {
            Protocol = S7Pdu.ProtocolId,
            Rosctr = S7Pdu.RosctrAckData,
            PduRef = _pduRef,
            Params = new byte[] { S7FunctionCode.WriteVar, (byte)count, 0x00, 0x00 },
            Data = resultItems.ToArray()
        };
        return resp.Serialize();
    }

    private byte[]? HandleSetupCommunication(S7Pdu pdu)
    {
        // Params: reserved(2) + maxAmqCalling(2) + maxAmqCalled(2) + pduLength(2) = 8
        var maxAmqCalling = (ushort)((pdu.Params[2] << 8) | pdu.Params[3]);
        var maxAmqCalled = (ushort)((pdu.Params[4] << 8) | pdu.Params[5]);
        var pduLength = (ushort)((pdu.Params[6] << 8) | pdu.Params[7]);
        if (pduLength > 960) pduLength = 960;

        // Standard setup response params: reserved(2) + echoed values(6)
        var respParams = new byte[]
        {
            0x00, 0x00,
            (byte)(maxAmqCalling >> 8), (byte)maxAmqCalling,
            (byte)(maxAmqCalled >> 8), (byte)maxAmqCalled,
            (byte)(pduLength >> 8), (byte)pduLength
        };

        var resp = new S7Pdu
        {
            Protocol = S7Pdu.ProtocolId,
            Rosctr = S7Pdu.RosctrAckData,
            PduRef = _pduRef,
            Params = respParams,
            Data = Array.Empty<byte>()
        };
        return resp.Serialize();
    }

    private byte[]? HandleReadSzl(S7Pdu pdu)
    {
        if (pdu.Params.Length < 6) return BuildErrorResponse(pdu, S7ReturnCode.AddressOutOfRange);

        var szlId = (ushort)((pdu.Params[2] << 8) | pdu.Params[3]);
        var szlData = _szlProvider.GetSzlData(szlId);

        if (szlData == null)
            return BuildErrorResponse(pdu, S7ReturnCode.ObjectNotFound);

        var resp = new S7Pdu
        {
            Protocol = S7Pdu.ProtocolId,
            Rosctr = S7Pdu.RosctrAckData,
            PduRef = _pduRef,
            Params = new byte[] { S7FunctionCode.ReadSzl, 0x07, 0x00, 0x00 },
            Data = szlData
        };
        return resp.Serialize();
    }

    private byte[] BuildErrorResponse(S7Pdu request, byte errorCode)
    {
        var resp = new S7Pdu
        {
            Protocol = S7Pdu.ProtocolId,
            Rosctr = S7Pdu.RosctrAck,
            PduRef = _pduRef,
            Params = new byte[] { request.Params.Length > 0 ? request.Params[0] : (byte)0, errorCode },
            Data = Array.Empty<byte>()
        };
        return resp.Serialize();
    }

    private TagDefinition? FindTag(S7Address addr)
    {
        return _tags.FirstOrDefault(t =>
        {
            if (!t.Enabled) return false;
            var parsed = S7AddressParser.Parse(t.Address);
            if (parsed == null) return false;
            return parsed.Area == addr.Area &&
                   parsed.DbNumber == addr.DbNumber &&
                   parsed.ByteOffset == addr.ByteOffset &&
                   (addr.SizeBits == 1 || parsed.SizeBits == addr.SizeBits);
        });
    }

    private byte[] TagValueToBytes(object value, string dataType, int expectedLen)
    {
        if (expectedLen == 0) expectedLen = 2;
        var bytes = new byte[expectedLen];

        try
        {
            switch (dataType)
            {
                case "Bool":
                    bytes[0] = Convert.ToBoolean(value) ? (byte)1 : (byte)0;
                    break;
                case "Byte":
                    bytes[0] = Convert.ToByte(value);
                    break;
                case "Int16":
                    var s16 = Convert.ToInt16(value);
                    bytes[0] = (byte)(s16 >> 8); bytes[1] = (byte)s16;
                    break;
                case "UInt16":
                    var u16 = Convert.ToUInt16(value);
                    bytes[0] = (byte)(u16 >> 8); bytes[1] = (byte)u16;
                    break;
                case "Int32":
                    var s32 = Convert.ToInt32(value);
                    bytes[0] = (byte)(s32 >> 24); bytes[1] = (byte)(s32 >> 16);
                    bytes[2] = (byte)(s32 >> 8); bytes[3] = (byte)s32;
                    break;
                case "UInt32":
                    var u32 = Convert.ToUInt32(value);
                    bytes[0] = (byte)(u32 >> 24); bytes[1] = (byte)(u32 >> 16);
                    bytes[2] = (byte)(u32 >> 8); bytes[3] = (byte)u32;
                    break;
                case "Float32":
                    var f = BitConverter.GetBytes(Convert.ToSingle(value));
                    if (BitConverter.IsLittleEndian) Array.Reverse(f);
                    f.CopyTo(bytes, 0);
                    break;
                case "Float64":
                    var d = BitConverter.GetBytes(Convert.ToDouble(value));
                    if (BitConverter.IsLittleEndian) Array.Reverse(d);
                    d.CopyTo(bytes, 0);
                    break;
                default:
                    var str = (value?.ToString() ?? "").PadRight(expectedLen, ' ')[..expectedLen];
                    System.Text.Encoding.ASCII.GetBytes(str).CopyTo(bytes, 0);
                    break;
            }
        }
        catch { }

        return bytes;
    }
}
