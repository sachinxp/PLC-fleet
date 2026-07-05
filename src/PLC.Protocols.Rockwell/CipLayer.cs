using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace PLC.Protocols.Rockwell;

public class CipLayer
{
    // CIP services
    public const byte GetAttributesAll = 0x01;
    public const byte SetAttributeSingle = 0x03;
    public const byte ReadTag = 0x4C;
    public const byte ReadTagFragmented = 0x4D;
    public const byte WriteTag = 0x4E;
    public const byte ReadModifyWriteTag = 0x4F;
    public const byte GetInstanceAttributesList = 0x55; // Browse

    // CIP general status codes
    public const byte Success = 0x00;
    public const byte ConnectionFailure = 0x01;
    public const byte ResourceUnavailable = 0x02;
    public const byte InvalidParameter = 0x03;
    public const byte PathSegmentError = 0x04;
    public const byte PathDestinationUnknown = 0x05;
    public const byte ServiceNotSupported = 0x08;
    public const byte InvalidAttribute = 0x0E;
    public const byte NotEnoughData = 0x13;
    public const byte InvalidParamValue = 0x20;

    public static byte[] BuildCipResponse(byte service, byte status, byte[] data)
    {
        // Reply: service | 0x80, reserved(0), status, extended status(0), data
        var resp = new byte[4 + data.Length];
        resp[0] = (byte)(service | 0x80);
        resp[1] = 0x00; // reserved
        resp[2] = status;
        resp[3] = 0x00; // extended status
        data.CopyTo(resp, 4);
        return resp;
    }

    public static byte[] BuildErrorResponse(byte service, byte status, ushort additionalStatus = 0)
    {
        return BuildCipResponse(service, status, new byte[] { (byte)additionalStatus, (byte)(additionalStatus >> 8) });
    }

    public static byte[] EncodeEpath(ushort classId, ushort instanceId, ushort attributeId = 0)
    {
        var path = new List<byte>();
        // Logical segment: class (0x20)
        if (classId <= 255) { path.Add(0x20); path.Add((byte)classId); }
        else { path.Add(0x21); path.Add((byte)classId); path.Add((byte)(classId >> 8)); }
        // Logical segment: instance (0x24)
        if (instanceId <= 255) { path.Add(0x24); path.Add((byte)instanceId); }
        else { path.Add(0x25); path.Add((byte)instanceId); path.Add((byte)(instanceId >> 8)); }
        if (attributeId > 0)
        {
            if (attributeId <= 255) { path.Add(0x30); path.Add((byte)attributeId); }
            else { path.Add(0x31); path.Add((byte)attributeId); path.Add((byte)(attributeId >> 8)); }
        }
        return path.ToArray();
    }

    public static byte[] EncodeReadTagRequest(string tagName, bool fragmented = false)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(tagName);
        var data = new byte[nameBytes.Length + 4];
        // ANSI extended symbol segment (0x91)
        data[0] = 0x91;
        data[1] = (byte)nameBytes.Length;
        nameBytes.CopyTo(data, 2);
        // Requested size: 0 = entire tag
        data[data.Length - 2] = 0x00;
        data[data.Length - 1] = 0x00;
        return data;
    }

    public static byte[] EncodeWriteTagRequest(string tagName, byte[] valueData)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(tagName);
        var data = new byte[nameBytes.Length + 4 + valueData.Length];
        data[0] = 0x91;
        data[1] = (byte)nameBytes.Length;
        nameBytes.CopyTo(data, 2);
        data[nameBytes.Length + 2] = 0x00; // offset size MSB
        data[nameBytes.Length + 3] = 0x00; // offset size LSB
        valueData.CopyTo(data, nameBytes.Length + 4);
        return data;
    }

    // Build tag list browse response
    public static byte[] BuildBrowseResponse(IEnumerable<string> tagNames, byte service = GetInstanceAttributesList)
    {
        var data = new List<byte>();
        foreach (var tag in tagNames)
        {
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(tag.PadRight(82, ' ')[..82]);
            // CIP symbolic name structure
            data.Add(0x91); // ANSI extended symbol
            data.Add((byte)nameBytes.Length);
            data.AddRange(nameBytes);
            // Data type (DINT = 0xC4)
            data.Add(0xC4);
            // Array dimensions (0 = scalar)
            data.Add(0x00); data.Add(0x00);
            // Size in bytes (4 for DINT)
            data.Add(0x04); data.Add(0x00);
            // Reserved/helper fields
            data.Add(0x00); data.Add(0x00);
            data.Add(0x00); data.Add(0x00);
        }
        return BuildCipResponse(service, Success, data.ToArray());
    }

    // Parse tag name from CIP request
    public static string? ParseTagName(byte[] reqData, out int offset)
    {
        offset = 0;
        if (reqData.Length < 2) return null;
        var segType = reqData[0];
        if (segType == 0x91) // ANSI extended symbol
        {
            var len = reqData[1];
            if (reqData.Length < 2 + len) return null;
            offset = 2 + len;
            return System.Text.Encoding.ASCII.GetString(reqData, 2, len);
        }
        return null;
    }
}
