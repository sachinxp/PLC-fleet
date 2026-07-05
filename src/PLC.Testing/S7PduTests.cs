using PLC.Protocols.S7;
using FluentAssertions;
using Xunit;

namespace PLC.Testing;

public class S7PduTests
{
    [Fact]
    public void Parse_ValidPdu_ReturnsCorrectFields()
    {
        // Minimal valid S7 PDU: header (8) + param len (2) + data len (2)
        var pdu = S7Pdu.Parse(new byte[] { 0x32, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00 });
        pdu.Should().NotBeNull();
        pdu!.Protocol.Should().Be(0x32);
        pdu.Rosctr.Should().Be(0x01);
        pdu.PduRef.Should().Be(1);
        pdu.ParamLen.Should().Be(2);
        pdu.DataLen.Should().Be(0);
    }

    [Fact]
    public void Parse_TooShort_ReturnsNull()
    {
        S7Pdu.Parse(new byte[] { 0x32, 0x01 }).Should().BeNull();
    }

    [Fact]
    public void Parse_WrongProtocol_ReturnsNull()
    {
        S7Pdu.Parse(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00 }).Should().BeNull();
    }

    [Fact]
    public void Serialize_RoundTrip()
    {
        var original = new S7Pdu
        {
            Protocol = 0x32,
            Rosctr = 0x03,
            PduRef = 42,
            Params = new byte[] { 0x04, 0x01, 0x00, 0x00 },
            Data = new byte[] { 0xFF, 0x04, 0x02, 0x00, 0x01 }
        };
        var bytes = original.Serialize();
        var parsed = S7Pdu.Parse(bytes);
        parsed.Should().NotBeNull();
        parsed!.Protocol.Should().Be(0x32);
        parsed.Rosctr.Should().Be(0x03);
        parsed.PduRef.Should().Be(42);
        parsed.ParamLen.Should().Be(4);
        parsed.DataLen.Should().Be(5);
    }

    [Fact]
    public void BuildReadVarParams_ProducesCorrectStructure()
    {
        var addr = new S7Address(S7Area.DataBlock, 0, 16, 1);
        var paramBytes = S7Pdu.BuildReadVarParams(1, new[] { addr });
        paramBytes.Should().HaveCount(13); // 1 (count) + 1 * 12
        paramBytes[0].Should().Be(1); // item count
        paramBytes[1].Should().Be(0x12); // spec type
        paramBytes[2].Should().Be(0x0A); // length
        paramBytes[4].Should().Be(0x02); // transport size byte
        paramBytes[5].Should().Be(2); // length in bytes for word
    }

    [Fact]
    public void BuildReadSzlParams_ReturnsCorrectBytes()
    {
        var paramBytes = S7Pdu.BuildReadSzlParams(0x0011);
        paramBytes.Should().HaveCount(6);
        paramBytes[0].Should().Be(0x1D);
        paramBytes[2].Should().Be(0x00);
        paramBytes[3].Should().Be(0x11);
    }
}
