using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace PLC.Protocols.OpcUa;

/// <summary>
/// Minimal OPC UA Binary protocol implementation for key services.
/// Enough to pass UaExpert discovery and browsing.
/// </summary>
public static class UaBinaryProtocol
{
    // Message types
    public const uint MessageHello = 0x48454C4F; // "HELO"
    public const uint MessageAcknowledge = 0x41434B4E; // "ACKN"
    public const uint MessageOpenSecureChannel = 0x4F504E43; // "OPNC"
    public const uint MessageCloseSecureChannel = 0x434C4F53; // "CLOS"
    public const uint MessageMessage = 0x4D534700; // "MSG\x00"

    // Service IDs
    public const uint GetEndpointsRequestId = 0x01;
    public const uint OpenSecureChannelRequestId = 0x0F;
    public const uint CloseSecureChannelRequestId = 0x10;
    public const uint CreateSessionRequestId = 0x1D;
    public const uint ActivateSessionRequestId = 0x1E;
    public const uint BrowseRequestId = 0x2B;
    public const uint ReadRequestId = 0x02;
    public const uint WriteRequestId = 0x04;
    public const uint CreateSubscriptionRequestId = 0x27;
    public const uint CreateMonitoredItemsRequestId = 0x29;

    // NodeIds
    public const uint RootFolderId = 84;
    public const uint ObjectsFolderId = 85;
    public const uint TypesFolderId = 86;
    public const uint ViewsFolderId = 87;

    public static byte[] EncodeHello(uint sendBufSize = 65536, uint recvBufSize = 65536, uint maxMessageSize = 0, uint maxChunkCount = 0, string endpointUrl = "opc.tcp://localhost:4840/")
    {
        var urlBytes = Encoding.ASCII.GetBytes(endpointUrl);
        var data = new byte[20 + urlBytes.Length];
        var s = data.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s[0..4], sendBufSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..8], recvBufSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..12], maxMessageSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..16], maxChunkCount);
        BinaryPrimitives.WriteUInt32LittleEndian(s[16..20], (uint)urlBytes.Length);
        urlBytes.CopyTo(data, 20);
        return MakeMessageChunk(MessageHello, data);
    }

    public static byte[] EncodeAcknowledge(uint sendBufSize = 65536, uint recvBufSize = 65536, uint maxMessageSize = 0, uint maxChunkCount = 0)
    {
        var data = new byte[16];
        var s = data.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s[0..4], sendBufSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..8], recvBufSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..12], maxMessageSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..16], maxChunkCount);
        return MakeMessageChunk(MessageAcknowledge, data);
    }

    public static byte[] EncodeGetEndpointsResponse(uint requestHandle, string serverUri)
    {
        var data = new List<byte>();
        // ResponseHeader: timestamp(8), requestHandle(4), serviceResult(4)
        data.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        data.AddRange(BitConverter.GetBytes(requestHandle));
        data.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // Good

        // EndpointDescription array (1 element)
        data.Add(0x01); data.Add(0x00); data.Add(0x00); data.Add(0x00);

        // EndpointUrl
        var urlBytes = Encoding.ASCII.GetBytes("opc.tcp://localhost:4840/");
        data.AddRange(EncodeString("opc.tcp://localhost:4840/"));
        // Server.ApplicationDescription
        data.AddRange(EncodeApplicationDescription(serverUri));
        // SecurityMode (1 = None)
        data.Add(0x01); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // SecurityPolicyUri (None)
        data.AddRange(EncodeString("http://opcfoundation.org/UA/SecurityPolicy#None"));
        // UserIdentityTokens (anonymous, 1)
        data.Add(0x01); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        data.AddRange(EncodeUserTokenPolicy());
        // TransportProfileUri
        data.AddRange(EncodeString("http://opcfoundation.org/UA-Profile/Transport/uatcp"));
        // SecurityLevel
        data.Add(0x00);

        return BuildServiceResponse(GetEndpointsRequestId, requestHandle, data.ToArray())!;
    }

    public static byte[] EncodeSessionResponse(uint requestHandle, uint sessionId, string serverUri, byte[] authToken)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        data.AddRange(BitConverter.GetBytes(requestHandle));
        data.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        // SessionId (NodeId numeric)
        data.Add(0x00); data.Add(0x01); // Encoding mask (TwoByte: 0x00, numeric)
        data.Add(0x01); // SessionId = 1 (TwoByte)

        // AuthenticationToken (NodeId numeric)
        data.Add(0x00); data.Add(0x01);
        data.Add(0x01);

        // RevisedSessionTimeout (double)
        data.AddRange(BitConverter.GetBytes(3600000.0));
        // MaxRequestMessageSize
        data.AddRange(BitConverter.GetBytes(65536u));
        // ServerDescription
        data.AddRange(EncodeApplicationDescription(serverUri));
        // ServerEndpoint (string)
        data.AddRange(EncodeString("opc.tcp://localhost:4840/"));
        // ServerArray (0)
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // NamespaceArray (1: http://opcfoundation.org/UA/ + simulator NS)
        data.AddRange(EncodeStringArray(new[] { "http://opcfoundation.org/UA/", "http://plc-simulator.local/UA/" }));
        // LocaleIds (0)
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // MinLocaleIds (0)
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // ServerNonce (empty byte string)
        data.Add(0xFF); data.Add(0xFF); data.Add(0xFF); data.Add(0xFF);
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // MaxBrowseContinuationPoints
        data.AddRange(BitConverter.GetBytes(10u));
        // MaxQueryContinuationPoints
        data.AddRange(BitConverter.GetBytes(10u));
        // MaxHistoryContinuationPoints
        data.AddRange(BitConverter.GetBytes(0u));
        // SessionTimeout (double)
        data.AddRange(BitConverter.GetBytes(60000.0));
        // OperationLimits (null)
        data.Add(0x00);

        return BuildServiceResponse(CreateSessionRequestId, requestHandle, data.ToArray())!;
    }

    public static byte[]? BuildServiceResponse(uint serviceId, uint requestHandle, byte[] responseData)
    {
        var body = new List<byte>();
        // ServiceId (uinteger)
        body.AddRange(BitConverter.GetBytes(serviceId));
        // Response data
        body.AddRange(responseData);

        var chunk = MakeSecureMessageChunk(MessageMessage, body.ToArray());
        return chunk;
    }

    private static byte[] MakeMessageChunk(uint messageType, byte[] data)
    {
        var headerSize = 8;
        var chunk = new byte[headerSize + data.Length];
        var s = chunk.AsSpan();
        s[0] = (byte)(messageType >> 24);
        s[1] = (byte)(messageType >> 16);
        s[2] = (byte)(messageType >> 8);
        s[3] = (byte)messageType;
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..8], (uint)chunk.Length);
        data.CopyTo(chunk, headerSize);
        return chunk;
    }

    private static byte[] MakeSecureMessageChunk(uint messageType, byte[] body)
    {
        var sequenceHeaderLen = 8;
        var headerSize = 8 + sequenceHeaderLen;

        var chunk = new byte[headerSize + body.Length];
        var s = chunk.AsSpan();
        s[0] = (byte)(messageType >> 24);
        s[1] = (byte)(messageType >> 16);
        s[2] = (byte)(messageType >> 8);
        s[3] = (byte)'F';
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..8], (uint)chunk.Length);

        // Sequence number & Request ID (no security for None mode)
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..12], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..16], 0);

        body.CopyTo(chunk, 16);
        return chunk;
    }

    public static byte[] EncodeOpenSecureChannelResponse(uint requestHandle, uint channelId)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        data.AddRange(BitConverter.GetBytes(requestHandle));
        data.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        // ChannelId
        data.AddRange(BitConverter.GetBytes(channelId));
        // TokenId
        data.AddRange(BitConverter.GetBytes(1u));
        // RevisedLifetime
        data.AddRange(BitConverter.GetBytes(3600000u));
        // Nonce (empty)
        data.Add(0xFF); data.Add(0xFF); data.Add(0xFF); data.Add(0xFF);
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // ServerProtocolVersion
        data.AddRange(BitConverter.GetBytes(0u));

        return BuildServiceResponse(OpenSecureChannelRequestId, requestHandle, data.ToArray())!;
    }

    // Browse helpers
    public static byte[] EncodeBrowseResponse(uint requestHandle, IEnumerable<UAReferenceDescription> references)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        data.AddRange(BitConverter.GetBytes(requestHandle));
        data.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // Good

        // ContinuationPoint (null)
        data.Add(0xFF); data.Add(0xFF); data.Add(0xFF); data.Add(0xFF);
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);

        // References array
        var refs = references.ToList();
        data.AddRange(BitConverter.GetBytes((uint)refs.Count));
        foreach (var r in refs) data.AddRange(r.Encode());

        return BuildServiceResponse(BrowseRequestId, requestHandle, data.ToArray())!;
    }

    // Read helpers
    public static byte[] EncodeReadResponse(uint requestHandle, IEnumerable<DataValue> values)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        data.AddRange(BitConverter.GetBytes(requestHandle));
        data.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        // Results array
        var vals = values.ToList();
        data.AddRange(BitConverter.GetBytes((uint)vals.Count));
        foreach (var v in vals) data.AddRange(v.Encode());

        // DiagnosticInfos (empty array)
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);

        return BuildServiceResponse(ReadRequestId, requestHandle, data.ToArray())!;
    }

    // Encoding helpers
    public static byte[] EncodeString(string? s)
    {
        if (s == null) return new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        var bytes = Encoding.UTF8.GetBytes(s);
        var result = new byte[4 + bytes.Length];
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(result, 0);
        bytes.CopyTo(result, 4);
        return result;
    }

    public static byte[] EncodeStringArray(string[] strings)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes((uint)strings.Length));
        foreach (var s in strings) data.AddRange(EncodeString(s));
        return data.ToArray();
    }

    public static byte[] EncodeApplicationDescription(string serverUri)
    {
        var data = new List<byte>();
        // ApplicationUri
        data.AddRange(EncodeString(serverUri));
        // ProductUri
        data.AddRange(EncodeString("http://plc-simulator.local/Product"));
        // ApplicationName (LocalizedText: locale+text)
        data.AddRange(EncodeLocalizedText("PLC Simulator"));
        // ApplicationType (0 = Server)
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // GatewayServerUri (null)
        data.AddRange(EncodeString(null));
        // DiscoveryProfileUri (null)
        data.AddRange(EncodeString(null));
        // DiscoveryUrls (0)
        data.Add(0x00); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        return data.ToArray();
    }

    public static byte[] EncodeLocalizedText(string text)
    {
        var data = new List<byte>();
        // Locale (null = empty)
        data.AddRange(EncodeString(""));
        // Text
        data.AddRange(EncodeString(text));
        return data.ToArray();
    }

    public static byte[] EncodeUserTokenPolicy()
    {
        var data = new List<byte>();
        // PolicyId
        data.AddRange(EncodeString("anonymous"));
        // TokenType (1 = anonymous)
        data.Add(0x01); data.Add(0x00); data.Add(0x00); data.Add(0x00);
        // IssuedTokenType (null)
        data.AddRange(EncodeString(null));
        // IssuerEndpointUrl (null)
        data.AddRange(EncodeString(null));
        // SecurityPolicyUri (null → same as endpoint)
        data.AddRange(EncodeString(null));
        return data.ToArray();
    }

    // Parse NodeId from binary encoding
    public static uint ParseNumericNodeId(byte[] data, int offset, out int consumed)
    {
        consumed = 1;
        if (offset >= data.Length) return 0;
        var encodingMask = data[offset];
        if ((encodingMask & 0x3F) == 0x00) // TwoByte
        {
            consumed = 2;
            return data[offset + 1];
        }
        if ((encodingMask & 0x3F) == 0x01) // FourByte
        {
            consumed = 4;
            return BitConverter.ToUInt16(data, offset + 2);
        }
        consumed = 1;
        return 0;
    }

    public static uint ParseRequestHandle(byte[] data, int baseOffset)
    {
        // RequestHandle is after response/request header
        // Typically at offset baseOffset + 8 (after timestamp + handle)
        if (baseOffset + 12 > data.Length) return 0;
        return BitConverter.ToUInt32(data, baseOffset + 8);
    }
}

public class UAReferenceDescription
{
    public uint ReferenceTypeId { get; set; }
    public bool IsForward { get; set; }
    public UANodeId NodeId { get; set; } = new();
    public string BrowseName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public uint NodeClass { get; set; } = 2; // Variable=2, Object=1

    public byte[] Encode()
    {
        var data = new List<byte>();
        // ReferenceTypeId (NodeId)
        data.Add(0x00); data.Add(0x01); data.Add((byte)ReferenceTypeId);
        // IsForward
        data.Add(IsForward ? (byte)1 : (byte)0);
        // NodeId
        data.AddRange(NodeId.Encode());
        // BrowseName (QualifiedName: ns + name)
        data.AddRange(BitConverter.GetBytes((ushort)NodeId.NamespaceIndex));
        data.AddRange(UaBinaryProtocol.EncodeString(BrowseName));
        // DisplayName (LocalizedText)
        data.AddRange(UaBinaryProtocol.EncodeLocalizedText(DisplayName));
        // NodeClass
        data.AddRange(BitConverter.GetBytes(NodeClass));
        // TypeDefinition (NodeId)
        data.Add(0x00); data.Add(0x00); // TwoByte: null
        return data.ToArray();
    }
}

public class UANodeId
{
    public ushort NamespaceIndex { get; set; }
    public uint Identifier { get; set; }
    public string StringId { get; set; } = "";

    public byte[] Encode()
    {
        if (Identifier <= 255)
        {
            return new byte[] { 0x00, (byte)NamespaceIndex, (byte)Identifier };
        }
        var data = new List<byte>();
        data.Add(0x01); data.Add((byte)NamespaceIndex);
        data.AddRange(BitConverter.GetBytes((ushort)Identifier));
        return data.ToArray();
    }
}

public class DataValue
{
    public object? Value { get; set; }
    public uint StatusCode { get; set; } = 0x00000000; // Good
    public DateTime SourceTimestamp { get; set; } = DateTime.UtcNow;

    public byte[] Encode()
    {
        var data = new List<byte>();
        // Encoding mask (1 = value, 4 = source timestamp)
        byte mask = 0x05;
        // Value: Variant
        data.Add(mask);
        // Variant encoding
        if (Value is bool b)
        {
            data.Add(0x01); // Boolean
            data.Add(b ? (byte)1 : (byte)0);
        }
        else if (Value is int i)
        {
            data.Add(0x06); // Int32
            data.AddRange(BitConverter.GetBytes(i));
        }
        else if (Value is float f)
        {
            data.Add(0x0A); // Float
            data.AddRange(BitConverter.GetBytes(f));
        }
        else if (Value is double d)
        {
            data.Add(0x0B); // Double
            data.AddRange(BitConverter.GetBytes(d));
        }
        else if (Value is string s)
        {
            data.Add(0x0C); // String
            data.AddRange(UaBinaryProtocol.EncodeString(s));
        }
        else
        {
            data.Add(0x06); // Int32 default
            data.AddRange(BitConverter.GetBytes(0));
        }
        // SourceTimestamp
        data.AddRange(BitConverter.GetBytes(SourceTimestamp.ToBinary()));
        return data.ToArray();
    }
}
