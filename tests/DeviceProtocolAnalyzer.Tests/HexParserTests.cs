using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class HexParserTests
{
    [Fact]
    public void ParseAcceptsWhitespaceAndCommaSeparatedBytes()
    {
        Assert.Equal(new byte[] { 0x02, 0x01, 0x03 }, HexParser.Parse("02, 01 03"));
    }

    [Fact]
    public void ParseRejectsOddLengthOrNonHexInput()
    {
        Assert.Throws<FormatException>(() => HexParser.Parse("02 1"));
        Assert.Throws<FormatException>(() => HexParser.Parse("02 GG"));
    }
}
