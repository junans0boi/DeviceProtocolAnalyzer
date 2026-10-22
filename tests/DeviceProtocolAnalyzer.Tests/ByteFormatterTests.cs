using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class ByteFormatterTests
{
    [Fact]
    public void ToDisplayTextNamesControlBytesWithoutChangingInput()
    {
        var data = new byte[] { 0x02, (byte)'K', 0x36, 0x03 };

        var display = ByteFormatter.ToDisplayText(data);

        Assert.Equal("<STX>K6<ETX>", display);
        Assert.Equal(new byte[] { 0x02, (byte)'K', 0x36, 0x03 }, data);
    }

    [Fact]
    public void ToHexUsesTwoDigitUppercaseBytes()
    {
        Assert.Equal("02 0A FF", ByteFormatter.ToHex(new byte[] { 0x02, 0x0A, 0xFF }));
    }
}
