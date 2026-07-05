using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Protocols.Modbus;
using PLC.Shared.Models;
using FluentAssertions;
using Xunit;

namespace PLC.Testing;

public class ModbusIntegrationTests
{
    private static int _nextPort = 15100;
    private static int NextPort() => Interlocked.Increment(ref _nextPort);

    private List<TagDefinition> CreateTestTags() => new()
    {
        new TagDefinition { Name = "HR_Speed", Address = "40001", DataType = "Int16", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 1500 } },
        new TagDefinition { Name = "Coil_01", Address = "1", DataType = "Bool", Enabled = true, Simulation = new SimulationConfig { Profile = "Toggle", IntervalMs = 1000 } },
        new TagDefinition { Name = "AI_Temp", Address = "30001", DataType = "Int16", Enabled = true, Simulation = new SimulationConfig { Profile = "Sine", LowLimit = 0, HighLimit = 100, PeriodMs = 10000 } }
    };

    [Fact]
    public async Task StartStop_Listener_LifecycleOk()
    {
        var listener = new ModbusListener(8, 1, 0);
        listener.Initialize(CreateTestTags());
        await listener.StartAsync(IPAddress.Loopback, NextPort());
        listener.ActiveConnections.Should().Be(0);
        listener.Stop();
    }

    [Fact]
    public async Task ConnectAndReadHoldingRegister_ReturnsResponse()
    {
        var tags = CreateTestTags();
        var listener = new ModbusListener(8, 1, 0);
        listener.Initialize(tags);
        var port = NextPort();
        // Push initial value
        listener.Handler.UpdateTagValue("HR_Speed", (short)1500);
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // Read Holding Register FC 03: start 0 (40001-40001), qty 1
        var request = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        await stream.WriteAsync(request);
        var resp = new byte[256];
        var read = await stream.ReadAsync(resp, 0, resp.Length);
        read.Should().BeGreaterThan(8);
        resp[7].Should().Be(0x03); // function code echo
        resp[8].Should().Be(0x02); // byte count (1 register = 2 bytes)
        // value 1500 = 0x05DC
        resp[9].Should().Be(0x05);
        resp[10].Should().Be(0xDC);

        listener.Stop();
    }

    [Fact]
    public async Task ConnectAndWriteSingleCoil_ReturnsEcho()
    {
        var listener = new ModbusListener(8, 1, 0);
        listener.Initialize(CreateTestTags());
        var port = NextPort();
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // Write Single Coil FC 05: coil 1, ON (0xFF00)
        var request = new byte[] { 0x00, 0x02, 0x00, 0x00, 0x00, 0x06, 0x01, 0x05, 0x00, 0x01, 0xFF, 0x00 };
        await stream.WriteAsync(request);
        var resp = new byte[256];
        var read = await stream.ReadAsync(resp, 0, resp.Length);
        read.Should().Be(12); // MBAP(7) + 4 data + 1 fc
        resp[7].Should().Be(0x05);
        resp[9].Should().Be(0x01); // address echo
        resp[10].Should().Be(0xFF); // value echo
        resp[11].Should().Be(0x00);

        listener.Stop();
    }

    [Fact]
    public async Task InvalidFunction_ReturnsException()
    {
        var listener = new ModbusListener(8, 1, 0);
        listener.Initialize(CreateTestTags());
        var port = NextPort();
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // Invalid FC 07
        var request = new byte[] { 0x00, 0x03, 0x00, 0x00, 0x00, 0x06, 0x01, 0x07, 0x00, 0x00, 0x00, 0x00 };
        await stream.WriteAsync(request);
        var resp = new byte[256];
        var read = await stream.ReadAsync(resp, 0, resp.Length);
        read.Should().Be(9);
        resp[7].Should().Be(0x87); // error flag
        resp[8].Should().Be(ModbusExceptionCode.IllegalFunction);

        listener.Stop();
    }

    [Fact]
    public async Task ConnectionLimit_DoesNotExceedMax()
    {
        var listener = new ModbusListener(1, 1, 0); // max 1 connection
        listener.Initialize(CreateTestTags());
        var port = NextPort();
        await listener.StartAsync(IPAddress.Loopback, port);

        var client1 = new TcpClient();
        await client1.ConnectAsync("127.0.0.1", port);
        await Task.Delay(200);
        listener.ActiveConnections.Should().BeLessThanOrEqualTo(1);

        var client2 = new TcpClient();
        await client2.ConnectAsync("127.0.0.1", port);
        await Task.Delay(200);
        listener.ActiveConnections.Should().BeLessThanOrEqualTo(1);

        client1.Dispose();
        client2.Dispose();
        listener.Stop();
    }
}
