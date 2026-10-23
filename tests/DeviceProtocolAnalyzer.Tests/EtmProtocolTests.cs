using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class EtmProtocolTests
{
    // Digits are the running total in units of 10 won, one decimal digit per byte.
    private static byte[] Frame(int tens)
        => [0xAA, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, (byte)(tens / 1000 % 10), (byte)(tens / 100 % 10), (byte)(tens / 10 % 10), (byte)(tens % 10), 0];

    [Fact]
    public void TotalIsReadFromTheFourDigitsInTensOfWon()
    {
        // Real capture: 241 N10 + 70x50 + 147x100 + 94x500 = 67,610 won, shown as 6 7 6 1.
        Assert.Equal(67610, EtmProtocol.TotalWon(Frame(6761)));
        Assert.Equal(0, EtmProtocol.TotalWon(Frame(0)));
    }

    [Fact]
    public void FramesSplitAcrossReadsAreReassembled()
    {
        var etm = new EtmProtocol();
        var frame = Frame(5);

        Assert.Empty(etm.Feed(frame[..4]));
        Assert.Empty(etm.Feed(frame[4..9]));
        var frames = etm.Feed(frame[9..]);

        Assert.Single(frames);
        Assert.Equal(frame, frames[0]);
    }

    [Fact]
    public void SeveralFramesInOneReadAndLeadingGarbageAreHandled()
    {
        var etm = new EtmProtocol();
        var data = new byte[] { 0x00, 0x80 }.Concat(Frame(1)).Concat(Frame(2)).ToArray();

        var frames = etm.Feed(data);

        Assert.Equal(2, frames.Count);
        Assert.Equal(20, EtmProtocol.TotalWon(frames[1]));
    }

    [Fact]
    public void IncreaseTellsWhichCoinWasCounted()
    {
        var etm = new EtmProtocol();

        Assert.Equal("합계 10원", etm.Describe(Frame(1)));
        Assert.Contains("+10원 10원(N10 포함)", etm.Describe(Frame(2)));
        Assert.Contains("+50원 50원", etm.Describe(Frame(7)));
        Assert.Contains("+100원 100원", etm.Describe(Frame(17)));
        Assert.Contains("+500원 500원", etm.Describe(Frame(67)));
    }

    [Fact]
    public void ResetReportsTheRunSummary()
    {
        var etm = new EtmProtocol();
        etm.Describe(Frame(1));
        etm.Describe(Frame(2));
        etm.Describe(Frame(7));
        etm.Describe(Frame(57));

        var reset = etm.Describe(Frame(0));

        Assert.Contains("리셋", reset);
        Assert.Contains("10원(N10 포함) 1개", reset);
        Assert.Contains("50원 1개", reset);
        Assert.Contains("500원 1개", reset);
        Assert.Contains("합계 570원", reset);
        Assert.Equal("리셋 (합계 0원)", etm.Describe(Frame(0)));
    }
}
