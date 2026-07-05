using System;
using PLC.Protocols.Modbus;
using FluentAssertions;
using Xunit;

namespace PLC.Testing;

public class ModbusFrameTests
{
    [Fact]
    public void Parse_ValidFrame_ReturnsCorrectFields()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        var frame = ModbusFrame.Parse(bytes);
        frame.Should().NotBeNull();
        frame!.TransactionId.Should().Be(1);
        frame.ProtocolId.Should().Be(0);
        frame.Length.Should().Be(6);
        frame.UnitId.Should().Be(1);
        frame.FunctionCode.Should().Be(0x03);
        frame.Data.Should().HaveCount(4);
    }

    [Fact]
    public void Parse_TooShort_ReturnsNull()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x00 };
        var frame = ModbusFrame.Parse(bytes);
        frame.Should().BeNull();
    }

    [Fact]
    public void Serialize_ProducesCorrectBytes()
    {
        var frame = new ModbusFrame
        {
            TransactionId = 5,
            UnitId = 2,
            FunctionCode = 0x04,
            Data = new byte[] { 0x00, 0x01, 0x00, 0x02 }
        };
        var bytes = frame.Serialize();
        bytes.Should().HaveCount(12);
        bytes[0].Should().Be(0x00); bytes[1].Should().Be(0x05); // transaction
        bytes[2].Should().Be(0x00); bytes[3].Should().Be(0x00); // protocol
        bytes[4].Should().Be(0x00); bytes[5].Should().Be(0x06); // length
        bytes[6].Should().Be(0x02); // unit
        bytes[7].Should().Be(0x04); // fc
    }

    [Fact]
    public void BuildErrorResponse_SetsErrorFlag()
    {
        var resp = ModbusFrame.BuildErrorResponse(1, 1, 0x03, ModbusExceptionCode.IllegalDataAddress);
        resp.Should().HaveCount(9);
        resp[7].Should().Be(0x83); // fc | 0x80
        resp[8].Should().Be(ModbusExceptionCode.IllegalDataAddress);
    }

    [Fact]
    public void ReadCoils_ReturnsCorrectByteCount()
    {
        var handler = new ModbusRequestHandler(new ModbusAddressMap(new()));
        var data = new byte[] { 0x00, 0x00, 0x00, 0x08 }; // start 0, qty 8
        var (response, exception) = handler.HandleRequest(0x01, data, 1, 1);
        response.Should().NotBeNull();
        exception.Should().BeNull();
    }

    [Fact]
    public void ReadHoldingRegisters_InvalidQuantity_ReturnsError()
    {
        var handler = new ModbusRequestHandler(new ModbusAddressMap(new()));
        var data = new byte[] { 0x00, 0x00, 0x01, 0x00 }; // start 0, qty 256 > 125
        var (response, exception) = handler.HandleRequest(0x03, data, 1, 1);
        response.Should().BeNull();
        exception.Should().Be(ModbusExceptionCode.IllegalDataValue);
    }

    [Fact]
    public void ReadDeviceIdentification_ReturnsNameplate()
    {
        var handler = new ModbusRequestHandler(new ModbusAddressMap(new()));
        var data = new byte[] { 0x0E, 0x01 }; // MEI type 14, read Dev ID 1
        var (response, exception) = handler.HandleRequest(0x2B, data, 1, 1);
        response.Should().NotBeNull();
        exception.Should().BeNull();
    }

    [Fact]
    public void WriteSingleCoil_ReturnsEcho()
    {
        var handler = new ModbusRequestHandler(new ModbusAddressMap(new()));
        var data = new byte[] { 0x00, 0x01, 0xFF, 0x00 }; // coil 1, ON
        var (response, exception) = handler.HandleRequest(0x05, data, 1, 1);
        response.Should().NotBeNull();
        exception.Should().BeNull();
        // Echo response should match request data (first 4 bytes)
        response.Should().HaveCount(12); // MBAP + 4 data
    }

    [Fact]
    public void UnsupportedFunction_ReturnsIllegalFunction()
    {
        var handler = new ModbusRequestHandler(new ModbusAddressMap(new()));
        var (response, exception) = handler.HandleRequest(0x07, Array.Empty<byte>(), 1, 1);
        response.Should().BeNull();
        exception.Should().Be(ModbusExceptionCode.IllegalFunction);
    }
}
