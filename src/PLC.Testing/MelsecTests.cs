using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Protocols.Melsec;
using PLC.Shared.Models;
using FluentAssertions;

namespace PLC.Testing;

public class McFrameTests
{
    [Fact]
    public void Parse_ValidFrame_ReturnsCorrectFields()
    {
        var bytes = new byte[] {
            0x00, 0x50, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x00,
            0x0C, 0x00, 0x10, 0x27,
            0x01, 0x04, 0x00, 0x00,
            0x4D, 0x00, 0x00, 0x08
        };
        var frame = McFrame.Parse(bytes);
        frame.Should().NotBeNull();
        frame!.Subheader.Should().Be(0x5000);
        frame.NetworkNo.Should().Be(0);
        frame.PlcNo.Should().Be(0xFF);
        frame.Command.Should().Be(0x0401);
        frame.Subcommand.Should().Be(0x0000);
    }

    [Fact]
    public void Parse_TooShort_ReturnsNull()
    {
        McFrame.Parse(new byte[] { 0x00, 0x50, 0x00 }).Should().BeNull();
    }

    [Fact]
    public void Serialize_RoundTrip()
    {
        var original = new McFrame
        {
            Subheader = McFrame.BatchReadBin,
            Command = 0x0401,
            Subcommand = 0x0000,
            NetworkNo = 0,
            PlcNo = 0xFF,
            IoNo = 0x03FF,
            StationNo = 0,
            MonitoringTimer = 10000,
            Data = new byte[] { 0x4D, 0x00, 0x00, 0x08 }
        };
        var bytes = original.Serialize();
        bytes.Length.Should().BeGreaterThan(16);
        var parsed = McFrame.Parse(bytes);
        parsed.Should().NotBeNull();
        parsed!.Subheader.Should().Be(McFrame.BatchReadBin);
        parsed.Command.Should().Be(0x0401);
    }

    [Fact]
    public void BuildResponse_SetsCorrectSubheader()
    {
        var resp = McFrame.BuildResponse(McFrame.BatchReadBin, 0x0401, 0x0000);
        resp.Length.Should().BeGreaterThan(14);
        var frame = McFrame.Parse(resp);
        frame.Should().NotBeNull();
    }

    [Fact]
    public void BuildErrorResponse_ReturnsNonEmpty()
    {
        var resp = McFrame.BuildErrorResponse(0x0401, 0x0000, 0xC05C);
        resp.Length.Should().BeGreaterThan(16);
    }

    [Fact]
    public void IsBitDevice_ReturnsTrueForBitDevices()
    {
        McFrame.IsBitDevice(McFrame.DeviceM).Should().BeTrue();
        McFrame.IsBitDevice(McFrame.DeviceX).Should().BeTrue();
        McFrame.IsBitDevice(McFrame.DeviceY).Should().BeTrue();
        McFrame.IsBitDevice(McFrame.DeviceB).Should().BeTrue();
        McFrame.IsBitDevice(McFrame.DeviceD).Should().BeFalse();
        McFrame.IsBitDevice(McFrame.DeviceW).Should().BeFalse();
    }

    [Fact]
    public void DeviceCodeToSize_ReturnsCorrectSizes()
    {
        McFrame.DeviceCodeToSize(McFrame.DeviceM).Should().Be(1);
        McFrame.DeviceCodeToSize(McFrame.DeviceD).Should().Be(2);
    }

    [Fact]
    public void BatchReadWord_ReturnsData()
    {
        var tags = new List<TagDefinition>
        {
            new TagDefinition { Name = "D0", Address = "D0", DataType = "Int16", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = (short)1234 } }
        };
        var handler = new MelsecHandler(tags);
        handler.UpdateTagValue("D0", (short)1234);
        var req = new McFrame
        {
            Subheader = McFrame.BatchReadBin,
            Command = 0x0403,
            Subcommand = 0x0000,
            Data = new byte[] { 0x44, 0x00, 0x00, 0x01 }
        };
        var response = handler.HandleRequest(req);
        response.Should().NotBeNull();
    }

    [Fact]
    public void BatchReadBit_ReturnsBitData()
    {
        var tags = new List<TagDefinition>
        {
            new TagDefinition { Name = "M0", Address = "M0", DataType = "Bool", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 1 } }
        };
        var handler = new MelsecHandler(tags);
        handler.UpdateTagValue("M0", true);
        var req = new McFrame
        {
            Subheader = McFrame.BatchReadBin,
            Command = 0x0401,
            Subcommand = 0x0000,
            Data = new byte[] { 0x4D, 0x00, 0x00, 0x01 }
        };
        var response = handler.HandleRequest(req);
        response.Should().NotBeNull();
    }

    [Fact]
    public void ModelNameRead_ReturnsModelName()
    {
        var handler = new MelsecHandler(new List<TagDefinition>(), "FX5U-32MT/ES");
        var req = new McFrame
        {
            Subheader = McFrame.BatchReadBin,
            Command = 0x0001,
            Subcommand = 0x0000
        };
        var response = handler.HandleRequest(req);
        response.Should().NotBeNull();
    }

    [Fact]
    public void UnsupportedCommand_ReturnsResponse()
    {
        var handler = new MelsecHandler(new List<TagDefinition>());
        var req = new McFrame
        {
            Subheader = McFrame.BatchReadBin,
            Command = 0xFFFF,
            Subcommand = 0x0000
        };
        var response = handler.HandleRequest(req);
        response.Should().NotBeNull();
        response.Length.Should().BeGreaterThan(14);
    }

    [Fact]
    public void BatchWriteWord_UpdatesTagValue()
    {
        var tags = new List<TagDefinition>
        {
            new TagDefinition { Name = "D0", Address = "D0", DataType = "Int16", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = (short)0 } }
        };
        var handler = new MelsecHandler(tags);
        var req = new McFrame
        {
            Subheader = McFrame.BatchWriteBin,
            Command = 0x1403,
            Subcommand = 0x0000,
            Data = new byte[] { 0x44, 0x00, 0x00, 0x01, 0x34, 0x12 }
        };
        var response = handler.HandleRequest(req);
        response.Should().NotBeNull();
    }
}

public class MelsecIntegrationTests
{
    private static int _portCounter = 15300;
    private static int NextPort() => Interlocked.Increment(ref _portCounter);

    private List<TagDefinition> CreateTestTags() => new()
    {
        new TagDefinition { Name = "D0", Address = "D0", DataType = "Int16", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = (short)1234 } },
        new TagDefinition { Name = "M0", Address = "M0", DataType = "Bool", Enabled = true, Simulation = new SimulationConfig { Profile = "Toggle", IntervalMs = 1000 } }
    };

    [Fact]
    public async Task StartStop_Listener_LifecycleOk()
    {
        var listener = new MelsecListener(CreateTestTags(), "FX5U-32MT/ES", 8, 1, 0);
        await listener.StartAsync(IPAddress.Loopback, NextPort());
        listener.ActiveConnections.Should().Be(0);
        listener.Stop();
    }

    [Fact]
    public async Task BatchReadWord_ReturnsValues()
    {
        var listener = new MelsecListener(CreateTestTags(), "FX5U-32MT/ES", 8, 1, 0);
        var port = NextPort();
        listener.Handler.UpdateTagValue("D0", (short)1234);
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        var request = new McFrame
        {
            Subheader = McFrame.BatchReadBin,
            Command = 0x0403,
            Subcommand = 0x0000,
            NetworkNo = 0,
            PlcNo = 0xFF,
            IoNo = 0x03FF,
            MonitoringTimer = 10000,
            Data = new byte[] { 0x44, 0x00, 0x00, 0x01 }
        };
        var reqBytes = request.Serialize();
        await stream.WriteAsync(reqBytes);

        var resp = new byte[256];
        var read = await stream.ReadAsync(resp, 0, resp.Length);
        read.Should().BeGreaterThan(16);

        listener.Stop();
    }

    [Fact]
    public async Task BatchWriteBit_WritesSuccessfully()
    {
        var listener = new MelsecListener(CreateTestTags(), "FX5U-32MT/ES", 8, 1, 0);
        var port = NextPort();
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        var request = new McFrame
        {
            Subheader = McFrame.BatchWriteBin,
            Command = 0x1401,
            Subcommand = 0x0000,
            NetworkNo = 0,
            PlcNo = 0xFF,
            IoNo = 0x03FF,
            MonitoringTimer = 10000,
            Data = new byte[] { 0x4D, 0x00, 0x00, 0x01, 0x01 }
        };
        var reqBytes = request.Serialize();
        await stream.WriteAsync(reqBytes);

        var resp = new byte[256];
        var read = await stream.ReadAsync(resp, 0, resp.Length);
        read.Should().BeGreaterThan(14);

        listener.Stop();
    }
}
