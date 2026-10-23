using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class KisanProtocolTests
{
    // Captured from a real counter: 1,000 won x 100 sheets, total 100,000 won.
    private static readonly byte[] RealSummary = Convert.FromHexString(
        "EA0C64" + "00000000000000000000000000000000000000000000000000000000" + "00" + "6400" + "0000" + "E803" + "A0860100" + "B3000000" + "EE")
        .ToArray();

    // Builds a summary frame with the checksum rule (0x7F xor payload[0..39]).
    private static byte[] Summary(int c1000, int c5000, int c10000, int c50000, int sheets, int rejects, int value)
    {
        var p = new byte[44];
        void Put(int offset, int v, int len) { for (var i = 0; i < len; i++) { p[offset + i] = (byte)(v >> (8 * i)); } }
        Put(0, c1000, 2);
        Put(2, c5000, 2);
        Put(4, c10000, 2);
        Put(6, c50000, 2);
        Put(30, sheets, 2);
        Put(32, rejects, 2);
        Put(34, value, 2);
        Put(36, (c1000 * 1000) + (c5000 * 5000) + (c10000 * 10000) + (c50000 * 50000), 4);
        var check = (byte)0x7F;
        for (var i = 0; i < 40; i++) { check ^= p[i]; }
        p[40] = check;
        return [0xEA, 0x0C, .. p, 0xEE];
    }

    [Fact]
    public void RealSummaryFromTheDeviceHasValidChecksumAndTotal()
    {
        // The hex above must describe a frame of exactly 47 bytes.
        Assert.Equal(47, RealSummary.Length);
        Assert.True(KisanProtocol.SummaryChecksumValid(RealSummary[2..^1]));
        Assert.Contains("합계 100,000원", KisanProtocol.Describe(RealSummary));
        Assert.Contains("1,000원 100장", KisanProtocol.Describe(RealSummary));
    }

    [Theory]
    [InlineData(0x04, "1,000원")]
    [InlineData(0x08, "5,000원")]
    [InlineData(0x0C, "10,000원")]
    [InlineData(0x10, "50,000원")]
    public void BanknoteFrameNamesTheDenomination(byte code, string expected)
    {
        Assert.Equal("지폐 " + expected, KisanProtocol.Describe([0xEA, 0x01, 0x01, 0x00, code, 0xEE]));
    }

    [Fact]
    public void RejectAndSignalsAreDescribed()
    {
        Assert.Equal("거부 1장 (사유 1)", KisanProtocol.Describe([0xEA, 0x02, 0x01, 0x00, 0xEE]));
        Assert.Contains("04", KisanProtocol.Describe([0xEA, 0x04, 0xEE]));
        Assert.Contains("0E", KisanProtocol.Describe([0xEA, 0x0E, 0xEE]));
    }

    [Fact]
    public void MixedSummaryAddsUpPerDenomination()
    {
        // Wireshark run: 5 sheets of each denomination = 330,000 won.
        var text = KisanProtocol.Describe(Summary(5, 5, 5, 5, 20, 0, 50000));

        Assert.Contains("1,000원 5장, 5,000원 5장, 10,000원 5장, 50,000원 5장", text);
        Assert.Contains("20장", text);
        Assert.Contains("합계 330,000원", text);
        Assert.DoesNotContain("거부", text);
        Assert.DoesNotContain("체크 불일치", text);
    }

    [Fact]
    public void ResetSummaryAndRejectCountAreShown()
    {
        Assert.StartsWith("집계 초기화", KisanProtocol.Describe(Summary(0, 0, 0, 0, 0, 0, 0)));
        Assert.Contains("거부 2장", KisanProtocol.Describe(Summary(98, 0, 0, 0, 98, 2, 1000)));
    }

    [Fact]
    public void ChecksumMismatchIsFlagged()
    {
        var frame = Summary(1, 0, 0, 0, 1, 0, 1000);
        frame[2 + 40] ^= 0xFF;

        Assert.Contains("체크 불일치", KisanProtocol.Describe(frame));
    }

    [Fact]
    public void FramesAreReassembledAcrossReadsAndResynchronised()
    {
        var kisan = new KisanProtocol();
        var summary = Summary(0, 0, 1, 0, 1, 0, 10000);
        byte[] data = [0x00, 0x7F, 0xEA, 0x01, 0x01, 0x00, 0x04, 0xEE, .. summary];

        var first = kisan.Feed(data[..5]);
        var second = kisan.Feed(data[5..30]);
        var third = kisan.Feed(data[30..]);

        Assert.Empty(first);
        Assert.Single(second);
        Assert.Single(third);
        Assert.Equal(0x0C, third[0][1]);
        Assert.Equal(47, third[0].Length);
    }
}
