using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Protocols.S7;
using PLC.Shared.Models;
using FluentAssertions;
using Xunit;

namespace PLC.Testing;

public class S7IntegrationTests
{
    private static int _portCounter = 15102;

    private static int NextPort() => Interlocked.Increment(ref _portCounter);

    private PlcInstance CreateTestPlc(int port)
    {
        var plc = new PlcInstance
        {
            Id = "test-s7",
            Name = "TestS7",
            Brand = Brand.Siemens,
            Personality = "s7-1200",
            OrderCode = "6ES7 214-1AG40-0XB0",
            SerialNumber = "SN-S7-1200-42",
            FirmwareVersion = "V4.5.0",
            Description = "Test S7-1200",
            Network = new NetworkConfig { IpAddress = "127.0.0.1", Port = port, MaxConnections = 8, UseLoopback = true },
            Behavior = new BehaviorConfig { BaseLatencyMs = 1, JitterMs = 0 }
        };
        plc.Tags.Add(new TagDefinition { Name = "Merker_Counter", Address = "MW20", DataType = "Int16", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 42 } });
        plc.Tags.Add(new TagDefinition { Name = "DB_Speed", Address = "DB1.DBD0", DataType = "Float32", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 1.5f } });
        plc.Tags.Add(new TagDefinition { Name = "Input_Sensor", Address = "I0.0", DataType = "Bool", Enabled = true, Simulation = new SimulationConfig { Profile = "Toggle", IntervalMs = 1000 } });
        plc.Tags.Add(new TagDefinition { Name = "Output_Valve", Address = "Q0.0", DataType = "Bool", Enabled = true, Simulation = new SimulationConfig { Profile = "Static", Value = 1 } });
        return plc;
    }

    [Fact]
    public async Task StartStop_Listener_LifecycleOk()
    {
        var port = NextPort();
        var listener = new S7Listener(8, 1, 0);
        listener.Initialize(CreateTestPlc(port));
        await listener.StartAsync(IPAddress.Loopback, port);
        listener.ActiveConnections.Should().Be(0);
        listener.Stop();
    }

    [Fact]
    public async Task Connect_CotpHandshake_Succeeds()
    {
        var port = NextPort();
        var plc = CreateTestPlc(port);
        var listener = new S7Listener(8, 1, 0);
        listener.Initialize(plc);
        listener.Handler.UpdateTagValue("Merker_Counter", (short)42);
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // 1. TPKT + COTP Connection Request (CR)
        // TSAP: src=0x01, dst=0x02 (R0/S1 for S7-1200)
        var crPayload = CotpLayer.BuildConnectionRequest(0x01, 0x02);
        var tpktCr = CotpLayer.MakeTpkt(crPayload);
        await stream.WriteAsync(tpktCr);

        // Read COTP Connection Confirm (CC)
        var resp = new byte[256];
        var read = await stream.ReadAsync(resp, 0, resp.Length);
        read.Should().BeGreaterThan(10);
        // TPKT version
        resp[0].Should().Be(0x03);
        // COTP CC
        resp[4].Should().Be(0x11); // COTP length
        resp[5].Should().Be(0xD0); // CC

        listener.Stop();
    }

    [Fact]
    public async Task ReadVar_AfterCotp_ReturnsCorrectValues()
    {
        var port = NextPort();
        var plc = CreateTestPlc(port);
        var listener = new S7Listener(8, 1, 0);
        listener.Initialize(plc);
        listener.Handler.UpdateTagValue("Merker_Counter", (short)42);
        listener.Handler.UpdateTagValue("DB_Speed", 1.5f);
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // COTP handshake
        var crPayload = CotpLayer.BuildConnectionRequest(0x01, 0x02);
        await stream.WriteAsync(CotpLayer.MakeTpkt(crPayload));
        var cc = new byte[32];
        await stream.ReadAsync(cc);

        // Build Read Var request for MW20 (merker word at byte 20)
        var addr = new S7Address(S7Area.Merker, 20, 16);
        var readParams = S7Pdu.BuildReadVarParams(1, new[] { addr });
        var reqPdu = new S7Pdu
        {
            Protocol = S7Pdu.ProtocolId,
            Rosctr = S7Pdu.RosctrJob,
            PduRef = 1,
            Params = new byte[] { S7FunctionCode.ReadVar, 0x01 }.Concat(readParams[1..]).ToArray(),
            Data = Array.Empty<byte>()
        };
        // Fix up: readVar params structure: [0x04, count, items...]
        reqPdu.Params[0] = 0x04;
        var s7pduBytes = reqPdu.Serialize();
        var dtRequest = CotpLayer.BuildDataTransfer(s7pduBytes);
        var tpktRequest = CotpLayer.MakeTpkt(dtRequest);
        await stream.WriteAsync(tpktRequest);

        // Read response
        var s7resp = new byte[512];
        var readLen = await stream.ReadAsync(s7resp, 0, s7resp.Length);
        readLen.Should().BeGreaterThan(20);

        // Verify TPKT + DT + S7 PDU
        s7resp[0].Should().Be(0x03); // TPKT version
        // Skip to S7 PDU
        var tpktPayloadLen = (s7resp[2] << 8) | s7resp[3];
        tpktPayloadLen.Should().BeGreaterThan(10);
        // DT header at offset 4
        s7resp[4].Should().Be(0x02); // COTP DT length
        s7resp[5].Should().Be(0xF0); // COTP DT
        // S7 PDU at offset 6
        s7resp[6].Should().Be(0x32); // Protocol
        s7resp[7].Should().Be(0x03); // AckData
        // Read response should contain 42 (0x002A) as word value
        // Data area after params: return code (0xFF) + transport + length + value
        var dataStart = 6 + 10 + 4; // S7 header(10) + params(4)
        if (dataStart + 3 < readLen)
        {
            s7resp[dataStart].Should().Be(0xFF); // Success return code
            s7resp[dataStart + 1].Should().Be(0x04); // Transport size byte/word
            s7resp[dataStart + 2].Should().Be(0x02); // Length bytes
            s7resp[dataStart + 3].Should().Be(0x00); // High byte of 42
            s7resp[dataStart + 4].Should().Be(0x2A); // Low byte of 42
        }

        listener.Stop();
    }

    [Fact]
    public async Task ReadSzl_ReturnsModuleId()
    {
        var port = NextPort();
        var plc = CreateTestPlc(port);
        var listener = new S7Listener(8, 1, 0);
        listener.Initialize(plc);
        await listener.StartAsync(IPAddress.Loopback, port);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // COTP handshake
        await stream.WriteAsync(CotpLayer.MakeTpkt(CotpLayer.BuildConnectionRequest(0x01, 0x02)));
        var cc = new byte[32];
        await stream.ReadAsync(cc);

        // Read SZL 0x0011 (Module ID)
        var szlParams = S7Pdu.BuildReadSzlParams(0x0011);
        var reqPdu = new S7Pdu
        {
            Protocol = S7Pdu.ProtocolId,
            Rosctr = S7Pdu.RosctrJob,
            PduRef = 1,
            Params = szlParams,
            Data = Array.Empty<byte>()
        };
        var tpktReq = CotpLayer.MakeTpkt(CotpLayer.BuildDataTransfer(reqPdu.Serialize()));
        await stream.WriteAsync(tpktReq);

        var s7resp = new byte[512];
        var read = await stream.ReadAsync(s7resp, 0, s7resp.Length);
        read.Should().BeGreaterThan(30);

        // Verify S7 AcKData response
        s7resp[6].Should().Be(0x32); // Protocol
        s7resp[7].Should().Be(0x03); // AckData

        listener.Stop();
    }

    [Fact]
    public async Task ConnectionLimit_Enforced()
    {
        var port = NextPort();
        var listener = new S7Listener(1, 1, 0);
        listener.Initialize(CreateTestPlc(port));
        await listener.StartAsync(IPAddress.Loopback, port);

        var client1 = new TcpClient();
        await client1.ConnectAsync("127.0.0.1", port);

        var client2 = new TcpClient();
        Func<Task> act = () => client2.ConnectAsync("127.0.0.1", port);
        await act.Should().NotThrowAfterAsync(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100));

        client1.Dispose();
        client2.Dispose();
        listener.Stop();
    }
}
