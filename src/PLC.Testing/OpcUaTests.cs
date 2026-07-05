using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PLC.Protocols.OpcUa;
using PLC.Shared.Models;
using FluentAssertions;

namespace PLC.Testing;

public class UaBinaryProtocolTests
{
    [Fact]
    public void EncodeHello_HasCorrectMessageType()
    {
        var hello = UaBinaryProtocol.EncodeHello();
        Encoding.ASCII.GetString(hello, 0, 4).Should().Be("HELO");
    }

    [Fact]
    public void EncodeAcknowledge_HasCorrectMessageType()
    {
        var ack = UaBinaryProtocol.EncodeAcknowledge();
        Encoding.ASCII.GetString(ack, 0, 4).Should().Be("ACKN");
    }

    [Fact]
    public void EncodeString_Null_ReturnsMinusOneLengthPrefix()
    {
        var bytes = UaBinaryProtocol.EncodeString(null);
        bytes.Length.Should().Be(4);
        BitConverter.ToInt32(bytes, 0).Should().Be(-1);
    }

    [Fact]
    public void EncodeString_Valid_ReturnsLengthPrefixedUtf8()
    {
        var bytes = UaBinaryProtocol.EncodeString("ABC");
        bytes.Length.Should().Be(7);
        BitConverter.ToUInt32(bytes, 0).Should().Be(3);
        Encoding.UTF8.GetString(bytes, 4, 3).Should().Be("ABC");
    }

    [Fact]
    public void ParseNumericNodeId_TwoByte_ReturnsValue()
    {
        var data = new byte[] { 0x00, 0x55 };
        var id = UaBinaryProtocol.ParseNumericNodeId(data, 0, out var consumed);
        id.Should().Be(0x55);
        consumed.Should().Be(2);
    }

    [Fact]
    public void ParseNumericNodeId_FourByte_ReturnsValue()
    {
        var data = new byte[] { 0x01, 0x00, 0xAA, 0xBB };
        var id = UaBinaryProtocol.ParseNumericNodeId(data, 0, out var consumed);
        id.Should().Be(0xBBAA);
        consumed.Should().Be(4);
    }

    [Fact]
    public void ParseNumericNodeId_UnknownMask_ReturnsZero()
    {
        var data = new byte[] { 0x03, 0x00 };
        var id = UaBinaryProtocol.ParseNumericNodeId(data, 0, out var consumed);
        id.Should().Be(0);
        consumed.Should().Be(1);
    }

    [Fact]
    public void UANodeId_Encode_TwoByte()
    {
        var nodeId = new UANodeId { NamespaceIndex = 0, Identifier = 42 };
        var bytes = nodeId.Encode();
        bytes.Length.Should().Be(3);
        bytes[0].Should().Be(0x00);
        bytes[1].Should().Be(0x00);
        bytes[2].Should().Be(42);
    }

    [Fact]
    public void UANodeId_Encode_FourByte()
    {
        var nodeId = new UANodeId { NamespaceIndex = 1, Identifier = 0x1234 };
        var bytes = nodeId.Encode();
        bytes.Length.Should().Be(4);
        bytes[0].Should().Be(0x01);
        bytes[1].Should().Be(0x01);
    }

    [Fact]
    public void UAReferenceDescription_Encode_ProducesBytes()
    {
        var desc = new UAReferenceDescription
        {
            ReferenceTypeId = 33,
            IsForward = true,
            NodeId = new UANodeId { NamespaceIndex = 2, Identifier = 100 },
            BrowseName = "Test",
            DisplayName = "Test Tag",
            NodeClass = 2
        };
        var bytes = desc.Encode();
        bytes.Length.Should().BeGreaterThan(20);
        bytes[0].Should().Be(0x00);
        bytes[1].Should().Be(0x01);
        bytes[2].Should().Be(33);
        bytes[3].Should().Be(0x01);
    }

    [Fact]
    public void DataValue_Encode_Bool()
    {
        var dv = new DataValue { Value = true };
        var bytes = dv.Encode();
        bytes[0].Should().Be(0x05);
        bytes[1].Should().Be(0x01);
        bytes[2].Should().Be(0x01);
    }

    [Fact]
    public void DataValue_Encode_Int32()
    {
        var dv = new DataValue { Value = 42 };
        var bytes = dv.Encode();
        bytes[1].Should().Be(0x06);
    }

    [Fact]
    public void DataValue_Encode_Float()
    {
        var dv = new DataValue { Value = 3.14f };
        var bytes = dv.Encode();
        bytes[1].Should().Be(0x0A);
    }

    [Fact]
    public void DataValue_Encode_Double()
    {
        var dv = new DataValue { Value = 3.14159 };
        var bytes = dv.Encode();
        bytes[1].Should().Be(0x0B);
    }

    [Fact]
    public void DataValue_Encode_String()
    {
        var dv = new DataValue { Value = "Hello" };
        var bytes = dv.Encode();
        bytes[1].Should().Be(0x0C);
    }

    [Fact]
    public void DataValue_Encode_Null_FallsBackToInt32()
    {
        var dv = new DataValue { Value = null };
        var bytes = dv.Encode();
        bytes[1].Should().Be(0x06);
    }
}

public class UaIntegrationTests
{
    private static int _portCounter = 15500;
    private static int NextPort() => Interlocked.Increment(ref _portCounter);

    private List<TagDefinition> CreateTestTags() => new()
    {
        new TagDefinition { Name = "Temperature", Address = "Temperature", DataType = "Float32", Enabled = true, Simulation = new SimulationConfig { Profile = "Sine", LowLimit = 0, HighLimit = 100, PeriodMs = 10000 } },
        new TagDefinition { Name = "Pressure", Address = "Pressure", DataType = "Float64", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 1013.25 } }
    };

    [Fact]
    public async Task StartStop_Server_LifecycleOk()
    {
        var server = new UaServer(CreateTestTags());
        await server.StartAsync(IPAddress.Loopback, NextPort());
        server.Stop();
    }

    [Fact]
    public async Task HeloAckn_Handshake_Succeeds()
    {
        var server = new UaServer(CreateTestTags());
        var port = NextPort();
        await server.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        var hello = UaBinaryProtocol.EncodeHello();
        await stream.WriteAsync(hello);

        var resp = new byte[24];
        var read = await stream.ReadAsync(resp, 0, 8);
        read.Should().Be(8);
        Encoding.ASCII.GetString(resp, 0, 4).Should().Be("ACKN");

        server.Stop();
    }

}
