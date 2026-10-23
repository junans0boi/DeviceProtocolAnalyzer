using System.Text;
using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class EagleEye7ProtocolTests
{
    // Frames below were captured from a real device (no ticket numbers).
    private const string DeviceUcs = "504C2D45470155435300000016164900" + "2D455945377D";
    private const string DeviceUii = "504C2D45470155494900000018" + "0100010000" + "2D455945373C";
    private const string DeviceUci =
        "504C2D45470155434900000044" + "6DF10E01" + "48595500" + "0001" + "06" +
        "001388000000" + "002710000001" + "00C350000000" + "0186A0000000" + "0493E0000000" + "07A120000000" +
        "0000" + "2D45594537" + "52";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static string Text(byte[] data) => Convert.ToHexString(data);

    [Fact]
    public void StartFramesMatchTheVendorProgram()
    {
        Assert.Equal("504C2D45470155445413000000012D455945372 60D".Replace(" ", ""), Text(EagleEye7Protocol.UseDataTransfer()));
        Assert.Equal("504C2D4547014346471900000000000101000000" + "2D455945372A0D", Text(EagleEye7Protocol.ConfigureDataTransfer()));
    }

    [Fact]
    public void AcknowledgementsMatchCapturedHostResponses()
    {
        Assert.Equal("504C2D454702554353120000002D45594537250D", Text(EagleEye7Protocol.BuildAcknowledgement(Hex(DeviceUcs))!));
        Assert.Equal("504C2D454702554949150000000100002D45594537330D", Text(EagleEye7Protocol.BuildAcknowledgement(Hex(DeviceUii))!));
        Assert.Equal("504C2D454702554349120000002D455945373F0D", Text(EagleEye7Protocol.BuildAcknowledgement(Hex(DeviceUci))!));
    }

    [Fact]
    public void TicketInformationIsNotAcknowledged()
    {
        Assert.Null(EagleEye7Protocol.BuildAcknowledgement(DeviceFrame("UTI", Ticket("1234567890123456", 6, 10000))));
    }

    [Fact]
    public void SplitSeparatesFramesSentInOneTransfer()
    {
        var chunk = Hex(DeviceUii).Concat(Hex(DeviceUcs)).Concat(Hex(DeviceUci)).ToArray();

        var frames = EagleEye7Protocol.Split(chunk);

        Assert.Equal(["UII", "UCS", "UCI"], frames.Select(EagleEye7Protocol.Command));
    }

    [Fact]
    public void TicketIsDecodedWithMaskedNumber()
    {
        var text = EagleEye7Protocol.Describe(DeviceFrame("UTI", Ticket("1234567890123456", 6, 10000)));

        Assert.Contains("현대", text);
        Assert.Contains("10,000원", text);
        Assert.Contains("123456789012****", text);
        Assert.DoesNotContain("1234567890123456", text);
    }

    [Fact]
    public void CountInformationAndStatusAreDecoded()
    {
        Assert.Equal("집계 HYU 총 1장 (10,000원×1) | 거부 0 | 이중 0", EagleEye7Protocol.Describe(Hex(DeviceUci)));
        Assert.Equal("계수 종료", EagleEye7Protocol.Describe(Hex(DeviceUcs)));
        Assert.Equal("전송 인덱스 1건 [0]", EagleEye7Protocol.Describe(Hex(DeviceUii)));
    }

    [Fact]
    public void MaskNumberHidesTheLastFourCharacters()
    {
        Assert.Equal("1234567890****", EagleEye7Protocol.MaskNumber("1234567890ABCD"));
        Assert.Equal("***", EagleEye7Protocol.MaskNumber("123"));
    }

    private static byte[] Ticket(string number, int type, int amount)
    {
        var body = new List<byte> { 0, 0, (byte)type, (byte)(amount >> 16), (byte)(amount >> 8), (byte)amount, (byte)number.Length };
        body.AddRange(Encoding.ASCII.GetBytes(number));
        body.AddRange(new byte[7]);
        return body.ToArray();
    }

    private static byte[] DeviceFrame(string command, byte[] body)
    {
        var frame = new List<byte>();
        frame.AddRange("PL-EG"u8.ToArray());
        frame.Add(1);
        frame.AddRange(Encoding.ASCII.GetBytes(command));
        frame.AddRange(new byte[] { 0, 0, 0, (byte)(18 + body.Length) });
        frame.AddRange(body);
        frame.AddRange("-EYE7"u8.ToArray());
        frame.Add(frame.Aggregate((byte)0, (acc, b) => (byte)(acc ^ b)));
        frame.Add(0x0D);
        return frame.ToArray();
    }
}
