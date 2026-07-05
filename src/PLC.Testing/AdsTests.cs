using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PLC.Protocols.Ads;
using PLC.Shared.Models;
using FluentAssertions;

namespace PLC.Testing;

public class AmsPacketParseTests
{
    [Fact]
    public void Parse_ValidPacket_ReturnsCorrectFields()
    {
        var bytes = new byte[32 + 4]; // header + 4 data bytes
        bytes[0] = 0xC0; bytes[1] = 0xA8; bytes[2] = 0x01; bytes[3] = 0x01;
        bytes[4] = 0x01; bytes[5] = 0x01; // Target NetId
        bytes[6] = 0x00; bytes[7] = 0x00; // Target port
        bytes[8] = 0xC0; bytes[9] = 0xA8; bytes[10] = 0x01; bytes[11] = 0x01;
        bytes[12] = 0x01; bytes[13] = 0x01; // Source NetId
        bytes[14] = 0x01; bytes[15] = 0x00; // Source port = 1
        bytes[16] = 0x01; bytes[17] = 0x00; // CommandId = 1
        bytes[18] = 0x00; bytes[19] = 0x00; // StateFlags
        bytes[20] = 0x04; bytes[21] = 0x00; bytes[22] = 0x00; bytes[23] = 0x00; // DataLength = 4
        bytes[24] = 0x00; bytes[25] = 0x00; bytes[26] = 0x00; bytes[27] = 0x00; // ErrorCode
        bytes[28] = 0x2A; bytes[29] = 0x00; bytes[30] = 0x00; bytes[31] = 0x00; // InvokeId = 42
        bytes[32] = 0xDE; bytes[33] = 0xAD; bytes[34] = 0xBE; bytes[35] = 0xEF; // Data

        var pkt = AmsPacket.Parse(bytes);
        pkt.Should().NotBeNull();
        pkt!.TargetNetId.Should().Equal(new byte[] { 0xC0, 0xA8, 0x01, 0x01, 0x01, 0x01 });
        pkt.TargetPort.Should().Be(0);
        pkt.SourcePort.Should().Be(1);
        pkt.CommandId.Should().Be(1);
        pkt.InvokeId.Should().Be(42);
        pkt.Data.Should().Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    }

    [Fact]
    public void Parse_TooShort_ReturnsNull()
    {
        AmsPacket.Parse(new byte[10]).Should().BeNull();
    }

    [Fact]
    public void Serialize_WritesCorrectBytes()
    {
        var pkt = new AmsPacket
        {
            TargetNetId = new byte[] { 192, 168, 1, 1, 1, 1 },
            SourceNetId = new byte[] { 192, 168, 1, 100, 1, 1 },
            TargetPort = 801,
            SourcePort = 800,
            CommandId = AmsPacket.ReadDeviceInfo,
            InvokeId = 100,
            Data = new byte[] { 0x01, 0x02, 0x03 }
        };
        var bytes = pkt.Serialize();
        bytes.Length.Should().Be(35); // 32 header + 3 data
        // TargetPort = 801 = 0x0321 (LE)
        bytes[6].Should().Be(0x21); bytes[7].Should().Be(0x03);
        // Command = 1
        bytes[16].Should().Be(0x01); bytes[17].Should().Be(0x00);
        // DataLength = 3
        bytes[20].Should().Be(0x03);
        // InvokeId = 100
        bytes[28].Should().Be(100);
        // Data
        bytes[32].Should().Be(0x01); bytes[33].Should().Be(0x02); bytes[34].Should().Be(0x03);

        // Full round-trip
        var parsed = AmsPacket.Parse(bytes);
        parsed.Should().NotBeNull();
        parsed!.CommandId.Should().Be(1);
        parsed.TargetPort.Should().Be(801);
        parsed.SourcePort.Should().Be(800);
        parsed.InvokeId.Should().Be(100);
        parsed.Data.Should().HaveCount(3);
    }

    [Fact]
    public void BuildResponse_SwapsSourceAndTarget()
    {
        var request = new AmsPacket
        {
            TargetNetId = new byte[] { 1, 2, 3, 4, 1, 1 },
            SourceNetId = new byte[] { 5, 6, 7, 8, 1, 1 },
            TargetPort = 800,
            SourcePort = 801
        };
        var resp = AmsPacket.BuildResponse(request, new byte[] { 0xFF });
        var parsed = AmsPacket.Parse(resp);
        parsed.Should().NotBeNull();
        parsed!.TargetPort.Should().Be(801);
        parsed.SourcePort.Should().Be(800);
        parsed.TargetNetId.Should().Equal(request.SourceNetId);
        parsed.SourceNetId.Should().Equal(request.TargetNetId);
    }

    [Fact]
    public void BuildErrorResponse_SetsErrorCode()
    {
        var request = new AmsPacket { InvokeId = 5 };
        var resp = AmsPacket.BuildErrorResponse(request, AmsPacket.ErrServiceNotSupported);
        var parsed = AmsPacket.Parse(resp);
        parsed.Should().NotBeNull();
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrServiceNotSupported);
        parsed.InvokeId.Should().Be(5);
    }

    [Fact]
    public void NetIdFromIp_ConvertsCorrectly()
    {
        var netId = AmsPacket.NetIdFromIp("192.168.1.100");
        netId.Should().Equal(new byte[] { 192, 168, 1, 100, 1, 1 });
    }
}

public class AdsTcpLayerTests
{
    [Fact]
    public void FrameAndTryParse_RoundTrip()
    {
        var amsPacket = new AmsPacket { InvokeId = 42 }.Serialize();
        var framed = AdsTcpLayer.Frame(amsPacket);
        framed.Length.Should().Be(4 + amsPacket.Length);

        var success = AdsTcpLayer.TryParse(framed, out var frameLen, out var parsed);
        success.Should().BeTrue();
        frameLen.Should().Be(framed.Length);
        parsed.Should().Equal(amsPacket);
    }

    [Fact]
    public void TryParse_TooShort_ReturnsFalse()
    {
        AdsTcpLayer.TryParse(new byte[] { 0x01 }, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_IncompletePacket_ReturnsFalse()
    {
        var framed = AdsTcpLayer.Frame(new AmsPacket().Serialize());
        AdsTcpLayer.TryParse(framed[..^1], out _, out _).Should().BeFalse();
    }
}

public class AdsHandlerTests
{
    [Fact]
    public void ReadDeviceInfo_ReturnsVersion()
    {
        var tags = new List<TagDefinition>();
        var handler = new AdsHandler(tags, "TestDevice", "3.1.4024.56");
        var request = new AmsPacket { CommandId = AmsPacket.ReadDeviceInfo, InvokeId = 1 };
        var response = handler.HandleRequest(request);
        response.Should().NotBeNull();
        // Response should be a valid AmsPacket with error 0 and data
        var parsed = AmsPacket.Parse(response!);
        parsed.Should().NotBeNull();
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrNoError);
        parsed.Data.Should().HaveCountGreaterThan(16);
        parsed.InvokeId.Should().Be(1);
    }

    [Fact]
    public void ReadState_ReturnsAdsState()
    {
        var tags = new List<TagDefinition>();
        var handler = new AdsHandler(tags);
        var request = new AmsPacket { CommandId = AmsPacket.ReadState, InvokeId = 1 };
        var response = handler.HandleRequest(request);
        response.Should().NotBeNull();
        var parsed = AmsPacket.Parse(response!);
        parsed.Should().NotBeNull();
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrNoError);
    }

    [Fact]
    public void Read_WithF003Handle_ReturnsTagValue()
    {
        var tags = new List<TagDefinition>
        {
            new TagDefinition { Name = "GVL_Speed", Address = "GVL_Speed", DataType = "Int32", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 100 } }
        };
        var handler = new AdsHandler(tags);
        handler.UpdateTagValue("GVL_Speed", 100);

        uint hash = 0;
        foreach (var c in "GVL_Speed") hash = (hash * 31) + c;
        hash &= 0x7FFFFFFF;

        // Construct data manually to avoid Span/Write issues
        var data = new byte[12];
        data[0] = 0x03; data[1] = 0xF0; data[2] = 0x00; data[3] = 0x00; // indexGroup=0xF003 (LE)
        data[4] = (byte)(hash & 0xFF); data[5] = (byte)((hash >> 8) & 0xFF);
        data[6] = (byte)((hash >> 16) & 0xFF); data[7] = (byte)((hash >> 24) & 0xFF);
        data[8] = 0x04; data[9] = 0x00; data[10] = 0x00; data[11] = 0x00; // read length=4

        var request = new AmsPacket { CommandId = AmsPacket.Read, InvokeId = 1, Data = data };
        var response = handler.HandleRequest(request);
        response.Should().NotBeNull();
        var parsed = AmsPacket.Parse(response!);
        parsed.Should().NotBeNull();
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrNoError);
    }

    [Fact]
    public void Read_WithUnknownHandle_ReturnsError()
    {
        var handler = new AdsHandler(new List<TagDefinition>());
        var data = new byte[12];
        data[0] = 0x03; data[1] = 0xF0; data[2] = 0x00; data[3] = 0x00; // indexGroup=0xF003 (LE)
        data[4] = 0xFF; data[5] = 0xFF; data[6] = 0xFF; data[7] = 0x7F; // non-existent handle
        data[8] = 0x04; data[9] = 0x00; data[10] = 0x00; data[11] = 0x00;

        var request = new AmsPacket { CommandId = AmsPacket.Read, InvokeId = 1, Data = data };
        var response = handler.HandleRequest(request);
        var parsed = AmsPacket.Parse(response!);
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrSymbolNotFound);
    }

    [Fact]
    public void Read_WithShortData_ReturnsError()
    {
        var handler = new AdsHandler(new List<TagDefinition>());
        var request = new AmsPacket { CommandId = AmsPacket.Read, InvokeId = 1, Data = new byte[] { 0x01 } };
        var response = handler.HandleRequest(request);
        var parsed = AmsPacket.Parse(response!);
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrClientPort);
    }

    [Fact]
    public void Write_WithF003Handle_UpdatesValue()
    {
        var tags = new List<TagDefinition>
        {
            new TagDefinition { Name = "GVL_Val", Address = "GVL_Val", DataType = "Int32", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 0 } }
        };
        var handler = new AdsHandler(tags);

        uint hash = 0;
        foreach (var c in "GVL_Val") hash = (hash * 31) + c;
        hash &= 0x7FFFFFFF;

        var data = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(data[0..4], 0xF003);
        BinaryPrimitives.WriteUInt32LittleEndian(data[4..8], hash);
        BinaryPrimitives.WriteUInt32LittleEndian(data[8..12], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data[12..16], 42);

        var request = new AmsPacket { CommandId = AmsPacket.Write, InvokeId = 1, Data = data };
        var response = handler.HandleRequest(request);
        var parsed = AmsPacket.Parse(response!);
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrNoError);
    }

    [Fact]
    public void AddNotification_ReturnsHandle()
    {
        var handler = new AdsHandler(new List<TagDefinition>());
        var request = new AmsPacket { CommandId = AmsPacket.AddNotification, InvokeId = 42 };
        var response = handler.HandleRequest(request);
        var parsed = AmsPacket.Parse(response!);
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrNoError);
    }

    [Fact]
    public void UnsupportedCommand_ReturnsError()
    {
        var handler = new AdsHandler(new List<TagDefinition>());
        var request = new AmsPacket { CommandId = 0x9999, InvokeId = 1 };
        var response = handler.HandleRequest(request);
        var parsed = AmsPacket.Parse(response!);
        parsed!.ErrorCode.Should().Be(AmsPacket.ErrServiceNotSupported);
    }
}

public class AdsIntegrationTests
{
    private static int _portCounter = 15400;
    private static int NextPort() => Interlocked.Increment(ref _portCounter);

    private List<TagDefinition> CreateTestTags() => new()
    {
        new TagDefinition { Name = "Main.Speed", Address = "Main.Speed", DataType = "Int32", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 1500 } }
    };

    [Fact]
    public async Task StartStop_Listener_LifecycleOk()
    {
        var listener = new AdsListener(CreateTestTags(), 8, 1, 0);
        await listener.StartAsync(IPAddress.Loopback, NextPort());
        listener.ActiveConnections.Should().Be(0);
        listener.Stop();
    }

    [Fact]
    public async Task ReadDeviceInfo_ReturnsValidData()
    {
        var listener = new AdsListener(CreateTestTags(), 8, 1, 0);
        var port = NextPort();
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        var request = new AmsPacket
        {
            TargetNetId = AmsPacket.NetIdFromIp("127.0.0.1"),
            SourceNetId = AmsPacket.NetIdFromIp("127.0.0.1"),
            TargetPort = 801,
            SourcePort = 800,
            CommandId = AmsPacket.ReadDeviceInfo,
            InvokeId = 1
        };
        var framedReq = AdsTcpLayer.Frame(request.Serialize());
        await stream.WriteAsync(framedReq);

        var lenPrefix = new byte[4];
        var read = await stream.ReadAsync(lenPrefix, 0, 4);
        read.Should().Be(4);
        var amsLen = (int)BitConverter.ToUInt32(lenPrefix, 0);
        amsLen.Should().BeGreaterThan(30);

        var respBytes = new byte[amsLen];
        read = await stream.ReadAsync(respBytes, 0, amsLen);
        read.Should().Be(amsLen);

        var pkt = AmsPacket.Parse(respBytes);
        pkt.Should().NotBeNull();
        pkt!.ErrorCode.Should().Be(AmsPacket.ErrNoError);
        pkt.Data.Should().HaveCountGreaterThan(16);

        listener.Stop();
    }
}
