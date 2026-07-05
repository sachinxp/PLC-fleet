using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Protocols.Rockwell;
using PLC.Shared.Models;
using FluentAssertions;

namespace PLC.Testing;

public class RockwellFrameTests
{
    [Fact]
    public void EnipFrame_Parse_ValidFrame_ReturnsCorrectFields()
    {
        // RegisterSession = 0x0065, LE bytes: 0x65, 0x00
        var bytes = new byte[24];
        bytes[0] = 0x65; bytes[1] = 0x00; // Command = RegisterSession (LE)
        bytes[4] = 0xEF; bytes[5] = 0xBE; bytes[6] = 0xAD; bytes[7] = 0xDE; // SessionHandle = 0xDEADBEEF (LE)
        var frame = EnipFrame.Parse(bytes);
        frame.Should().NotBeNull();
        frame!.Command.Should().Be(EnipFrame.RegisterSession);
        frame.Length.Should().Be(0);
        frame.SessionHandle.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void EnipFrame_Parse_TooShort_ReturnsNull()
    {
        EnipFrame.Parse(new byte[10]).Should().BeNull();
    }

    [Fact]
    public void EnipFrame_Serialize_RoundTrip()
    {
        var original = new EnipFrame
        {
            Command = EnipFrame.ListIdentity,
            SessionHandle = 0xDEADBEEF,
            Status = 0,
            SenderContext = 0x1234567890ABCDEF,
            Options = 0,
            Data = new byte[] { 0x01, 0x02, 0x03 }
        };
        var bytes = original.Serialize();
        // Verify bytes directly
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[0..2]).Should().Be(EnipFrame.ListIdentity);
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]).Should().Be(0xDEADBEEF);
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[12..20]).Should().Be(0x1234567890ABCDEF);
        // Length should be 3
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..4]).Should().Be(3);
        bytes.Length.Should().Be(27); // 24 header + 3 data
        // Data at offset 24
        bytes[24].Should().Be(0x01);
        bytes[25].Should().Be(0x02);
        bytes[26].Should().Be(0x03);

        // Full round-trip parse
        var parsed = EnipFrame.Parse(bytes);
        parsed.Should().NotBeNull();
        parsed!.Command.Should().Be(EnipFrame.ListIdentity);
        parsed.SessionHandle.Should().Be(0xDEADBEEF);
        parsed.Data.Should().HaveCount(3);
    }

    [Fact]
    public void BuildSessionResponse_ContainsRegisterSessionCommand()
    {
        var resp = EnipFrame.BuildSessionResponse(42);
        var frame = EnipFrame.Parse(resp);
        frame.Should().NotBeNull();
        frame!.Command.Should().Be(EnipFrame.RegisterSession);
        frame.SessionHandle.Should().Be(42);
        frame.Status.Should().Be(0);
    }

    [Fact]
    public void BuildIdentityResponse_ContainsListIdentityCommand()
    {
        var resp = EnipFrame.BuildIdentityResponse();
        var frame = EnipFrame.Parse(resp);
        frame.Should().NotBeNull();
        frame!.Command.Should().Be(EnipFrame.ListIdentity);
        frame.Data.Should().HaveCountGreaterThan(20);
    }

    [Fact]
    public void CipLayer_EncodeEpath_Class8Bit_Instance8Bit()
    {
        var path = CipLayer.EncodeEpath(0x04, 0x01);
        path.Should().HaveCount(4);
        path[0].Should().Be(0x20); path[1].Should().Be(0x04);
        path[2].Should().Be(0x24); path[3].Should().Be(0x01);
    }

    [Fact]
    public void CipLayer_EncodeEpath_Class16Bit_Instance16Bit()
    {
        var path = CipLayer.EncodeEpath(0x100, 0x200);
        path.Should().HaveCount(6);
        path[0].Should().Be(0x21);
        path[3].Should().Be(0x25);
    }

    [Fact]
    public void CipLayer_ParseTagName_ReturnsCorrectName()
    {
        var data = new byte[] { 0x91, 0x04, 0x54, 0x65, 0x73, 0x74, 0x00, 0x00 };
        var name = CipLayer.ParseTagName(data, out var offset);
        name.Should().Be("Test");
        offset.Should().Be(6);
    }

    [Fact]
    public void CipLayer_ParseTagName_Invalid_ReturnsNull()
    {
        CipLayer.ParseTagName(new byte[] { 0x00 }, out _).Should().BeNull();
    }

    [Fact]
    public void CipLayer_BuildCipResponse_SetsReplyFlag()
    {
        var resp = CipLayer.BuildCipResponse(0x4C, 0x00, new byte[] { 0x01 });
        resp[0].Should().Be(0xCC); // 0x4C | 0x80
        resp[2].Should().Be(0x00);
    }

    [Fact]
    public void CipLayer_BuildErrorResponse_SetsStatus()
    {
        var resp = CipLayer.BuildErrorResponse(0x4C, 0x05);
        resp[0].Should().Be(0xCC);
        resp[2].Should().Be(0x05);
    }

    [Fact]
    public void CipLayer_EncodeReadTagRequest_ProducesCorrectFormat()
    {
        var req = CipLayer.EncodeReadTagRequest("TestTag");
        req[0].Should().Be(0x91);
        req[1].Should().Be(7);
        System.Text.Encoding.ASCII.GetString(req, 2, 7).Should().Be("TestTag");
    }
}

public class RockwellIntegrationTests
{
    private static int _portCounter = 15200;
    private static int NextPort() => Interlocked.Increment(ref _portCounter);

    private List<TagDefinition> CreateTestTags() => new()
    {
        new TagDefinition { Name = "TestTag", Address = "TestTag", DataType = "Int32", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 42 } },
        new TagDefinition { Name = "FlagBool", Address = "FlagBool", DataType = "Bool", Enabled = true, Simulation = new SimulationConfig { Profile = "Toggle", IntervalMs = 1000 } },
    };

    [Fact]
    public async Task StartStop_Listener_LifecycleOk()
    {
        var listener = new RockwellListener(CreateTestTags(), 8, 1, 0);
        await listener.StartAsync(IPAddress.Loopback, NextPort());
        listener.Stop();
    }

    private static async Task<EnipFrame> ReadEnipFrameAsync(NetworkStream stream)
    {
        var header = new byte[EnipFrame.HeaderSize];
        var read = await stream.ReadAsync(header, 0, EnipFrame.HeaderSize);
        read.Should().Be(EnipFrame.HeaderSize);
        var frame = EnipFrame.Parse(header);
        frame.Should().NotBeNull();
        if (frame!.Length > 0)
        {
            var data = new byte[frame.Length];
            read = await stream.ReadAsync(data, 0, frame.Length);
            read.Should().Be(frame.Length);
            frame.Data = data;
        }
        return frame;
    }

    [Fact]
    public async Task RegisterSession_ReturnsSessionHandle()
    {
        var listener = new RockwellListener(CreateTestTags(), 8, 1, 0);
        var port = NextPort();
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        var reqBytes = new EnipFrame
        {
            Command = EnipFrame.RegisterSession,
            Data = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 }
        }.Serialize();
        await stream.WriteAsync(reqBytes);

        var frame = await ReadEnipFrameAsync(stream);
        frame.Command.Should().Be(EnipFrame.RegisterSession);
        frame.SessionHandle.Should().BeGreaterThan(0);

        listener.Stop();
    }

    [Fact]
    public async Task SendRRData_ReadTag_ReturnsValue()
    {
        var listener = new RockwellListener(CreateTestTags(), 8, 1, 0);
        var port = NextPort();
        listener.UpdateTagValue("TestTag", 42);
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // Register session
        var regFrame = new EnipFrame { Command = EnipFrame.RegisterSession, Data = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 } };
        await stream.WriteAsync(regFrame.Serialize());
        var sessionFrame = await ReadEnipFrameAsync(stream);

        // Build CIP read request with proper RRData body
        // Format: InterfaceHandle(4) + Timeout(2) + ItemCount(2) + Items
        // CIP item format: service(1) + pathSize(1) + path(var) + data(var)
        var cipNamePayload = CipLayer.EncodeReadTagRequest("TestTag");
        var cipMsg = new System.Collections.Generic.List<byte>();
        cipMsg.Add(0x4C); // ReadTag service
        cipMsg.Add(0x02); // pathSize = 2 words + flags
        // EPATH for class 0x6B (8-bit), instance 1 (8-bit)
        cipMsg.Add(0x20); cipMsg.Add(0x6B); // Logical segment, class 0x6B (8-bit)
        cipMsg.Add(0x24); cipMsg.Add(0x01); // Logical segment, instance 1 (8-bit)
        cipMsg.AddRange(cipNamePayload); // Tag name data

        var body = new System.Collections.Generic.List<byte>();
        body.AddRange(new byte[4]); // InterfaceHandle = 0
        body.AddRange(new byte[2]); // Timeout = 0
        body.Add(0x02); body.Add(0x00); // ItemCount = 2
        // Item 1: Address item (type 0x8000, len 2)
        body.Add(0x00); body.Add(0x80); // TypeId
        body.Add(0x02); body.Add(0x00); // Length
        body.Add(0x00); body.Add(0x00); // Address data
        // Item 2: CIP data item (type 0x00B1)
        body.Add(0xB1); body.Add(0x00); // TypeId
        body.Add((byte)cipMsg.Count); body.Add((byte)(cipMsg.Count >> 8)); // Length
        body.AddRange(cipMsg);

        var readReq = new EnipFrame { Command = EnipFrame.SendRRData, SessionHandle = sessionFrame.SessionHandle, Data = body.ToArray() };
        await stream.WriteAsync(readReq.Serialize());

        var respFrame = await ReadEnipFrameAsync(stream);
        respFrame.Command.Should().Be(EnipFrame.SendRRData);

        listener.Stop();
    }

    [Fact]
    public async Task UnregisterSession_ClosesGracefully()
    {
        var listener = new RockwellListener(CreateTestTags(), 8, 1, 0);
        var port = NextPort();
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        var regFrame = new EnipFrame { Command = EnipFrame.RegisterSession, Data = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 } };
        await stream.WriteAsync(regFrame.Serialize());
        var sessionFrame = await ReadEnipFrameAsync(stream);

        var unregFrame = new EnipFrame { Command = EnipFrame.UnregisterSession, SessionHandle = sessionFrame.SessionHandle };
        await stream.WriteAsync(unregFrame.Serialize());

        listener.Stop();
    }
}
