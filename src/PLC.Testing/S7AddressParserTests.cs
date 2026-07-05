using PLC.Protocols.S7;
using FluentAssertions;
using Xunit;

namespace PLC.Testing;

public class S7AddressParserTests
{
    [Fact] public void Parse_DB_DataBlock() { var a = S7AddressParser.Parse("DB1.DBX0.0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.DataBlock); a.DbNumber.Should().Be(1); a.ByteOffset.Should().Be(0); a.BitOffset.Should().Be(0); }
    [Fact] public void Parse_DB_Bit_WithOffset() { var a = S7AddressParser.Parse("DB1.DBX100.5"); a.Should().NotBeNull(); a!.DbNumber.Should().Be(1); a.ByteOffset.Should().Be(100); a.BitOffset.Should().Be(5); }
    [Fact] public void Parse_DB_Word() { var a = S7AddressParser.Parse("DB1.DBW0"); a.Should().NotBeNull(); a!.SizeBits.Should().Be(16); a.DbNumber.Should().Be(1); }
    [Fact] public void Parse_DB_DWord() { var a = S7AddressParser.Parse("DB1.DBD0"); a.Should().NotBeNull(); a!.SizeBits.Should().Be(32); }
    [Fact] public void Parse_DB_Byte() { var a = S7AddressParser.Parse("DB1.DBB0"); a.Should().NotBeNull(); a!.SizeBits.Should().Be(8); }
    [Fact] public void Parse_Input_Bit() { var a = S7AddressParser.Parse("I0.0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Input); a.ByteOffset.Should().Be(0); a.BitOffset.Should().Be(0); a.SizeBits.Should().Be(1); }
    [Fact] public void Parse_Input_Word() { var a = S7AddressParser.Parse("IW0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Input); a.SizeBits.Should().Be(16); }
    [Fact] public void Parse_Input_DWord() { var a = S7AddressParser.Parse("ID0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Input); a.SizeBits.Should().Be(32); }
    [Fact] public void Parse_Input_Byte() { var a = S7AddressParser.Parse("IB0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Input); a.SizeBits.Should().Be(8); }
    [Fact] public void Parse_Output_Bit() { var a = S7AddressParser.Parse("Q0.0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Output); a.ByteOffset.Should().Be(0); a.BitOffset.Should().Be(0); }
    [Fact] public void Parse_Output_Word() { var a = S7AddressParser.Parse("QW0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Output); a.SizeBits.Should().Be(16); }
    [Fact] public void Parse_Output_DWord() { var a = S7AddressParser.Parse("QD0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Output); a.SizeBits.Should().Be(32); }
    [Fact] public void Parse_Merker_Bit() { var a = S7AddressParser.Parse("M0.0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Merker); a.ByteOffset.Should().Be(0); a.BitOffset.Should().Be(0); }
    [Fact] public void Parse_Merker_Word() { var a = S7AddressParser.Parse("MW20"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Merker); a.SizeBits.Should().Be(16); a.ByteOffset.Should().Be(20); }
    [Fact] public void Parse_Merker_DWord() { var a = S7AddressParser.Parse("MD40"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Merker); a.SizeBits.Should().Be(32); a.ByteOffset.Should().Be(40); }
    [Fact] public void Parse_Merker_Byte() { var a = S7AddressParser.Parse("MB0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Merker); a.SizeBits.Should().Be(8); }
    [Fact] public void Parse_Timer() { var a = S7AddressParser.Parse("T0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Timer); a.SizeBits.Should().Be(16); }
    [Fact] public void Parse_Counter() { var a = S7AddressParser.Parse("C0"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Counter); a.SizeBits.Should().Be(16); }
    [Fact] public void Parse_Counter_HighNumber() { var a = S7AddressParser.Parse("C999"); a.Should().NotBeNull(); a!.Area.Should().Be(S7Area.Counter); a.ByteOffset.Should().Be(999); }
    [Fact] public void Parse_Input_Bit_HighOffset() { var a = S7AddressParser.Parse("I12.7"); a.Should().NotBeNull(); a!.ByteOffset.Should().Be(12); a.BitOffset.Should().Be(7); }
    [Fact] public void Parse_Input_Bit_NoBit_DefaultsByte() { var a = S7AddressParser.Parse("I0"); a.Should().NotBeNull(); a!.SizeBits.Should().Be(8); a.Area.Should().Be(S7Area.Input); }
    [Fact] public void Parse_Empty_ReturnsNull() { S7AddressParser.Parse("").Should().BeNull(); }
    [Fact] public void Parse_Null_ReturnsNull() { S7AddressParser.Parse(null!).Should().BeNull(); }
    [Fact] public void Parse_Invalid_ReturnsNull() { S7AddressParser.Parse("ZZZ123").Should().BeNull(); }

    [Fact]
    public void ToString_AllAreas_ProducesCorrectString()
    {
        new S7Address(S7Area.Input, 0, 1, 0, 0).ToString().Should().Be("I0.0");
        new S7Address(S7Area.Input, 0, 16, 0, 0).ToString().Should().Be("IW0");
        new S7Address(S7Area.Input, 0, 32, 0, 0).ToString().Should().Be("ID0");
        new S7Address(S7Area.Output, 0, 1, 0, 3).ToString().Should().Be("Q0.3");
        new S7Address(S7Area.Output, 0, 16, 0, 0).ToString().Should().Be("QW0");
        new S7Address(S7Area.Merker, 100, 1, 0, 5).ToString().Should().Be("M100.5");
        new S7Address(S7Area.Merker, 20, 16, 0, 0).ToString().Should().Be("MW20");
        new S7Address(S7Area.DataBlock, 0, 1, 1, 0).ToString().Should().Be("DB1.DBX0.0");
        new S7Address(S7Area.DataBlock, 0, 16, 1, 0).ToString().Should().Be("DB1.DBW0");
        new S7Address(S7Area.DataBlock, 0, 32, 1, 0).ToString().Should().Be("DB1.DBD0");
        new S7Address(S7Area.DataBlock, 0, 8, 1, 0).ToString().Should().Be("DB1.DBB0");
        new S7Address(S7Area.Timer, 5, 16).ToString().Should().Be("T5");
        new S7Address(S7Area.Counter, 10, 16).ToString().Should().Be("C10");
    }
}
