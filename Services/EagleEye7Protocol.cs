using System.Text;

namespace DeviceProtocolAnalyzer.Services;

/// <summary>
/// Framing, acknowledgement and human-readable decoding for the EagleEye7 USB protocol,
/// derived from USB captures and the vendor's session logs.
/// Frame: "PL-EG" + flag(1=request, 2=response) + 3-char command + length(4) + body + "-EYE7" + XOR checksum [+ CR].
/// </summary>
public static class EagleEye7Protocol
{
    public const byte Request = 1;

    public const byte Response = 2;

    private static readonly byte[] StartMark = "PL-EG"u8.ToArray();

    private static readonly byte[] EndMark = "-EYE7"u8.ToArray();

    private static readonly string[] ConfigureNames =
        ["시작/종료", "스택·거부 센서", "계수 정보", "상품권 정보", "일련번호 이미지", "거부 사유", "오류 코드"];

    public static byte[] BuildFrame(byte flag, string command, byte[]? body = null)
    {
        body ??= [];
        var frame = new List<byte>();
        frame.AddRange(StartMark);
        frame.Add(flag);
        frame.AddRange(Encoding.ASCII.GetBytes(command));
        // Host frames carry the length little-endian (matches captures); it excludes checksum and CR.
        frame.AddRange(BitConverter.GetBytes(18 + body.Length));
        frame.AddRange(body);
        frame.AddRange(EndMark);
        frame.Add(Xor(frame));
        frame.Add(0x0D);
        return frame.ToArray();
    }

    /// <summary>Enable data transfer from the device (UDT).</summary>
    public static byte[] UseDataTransfer() => BuildFrame(Request, "UDT", [1]);

    /// <summary>Ask the device to push count info and ticket info only (the flags the vendor program uses).</summary>
    public static byte[] ConfigureDataTransfer() => BuildFrame(Request, "CFG", [0, 0, 1, 1, 0, 0, 0]);

    /// <summary>Splits one USB transfer into frames; a transfer may carry several frames back to back.</summary>
    // shortcut: assumes whole frames per transfer and no "PL-EG" inside a body, upgrade if partial frames show up.
    public static IReadOnlyList<byte[]> Split(byte[] data)
    {
        var frames = new List<byte[]>();
        var start = IndexOf(data, StartMark, 0);
        while (start >= 0)
        {
            var next = IndexOf(data, StartMark, start + 1);
            var end = next >= 0 ? next : data.Length;
            frames.Add(data[start..end]);
            start = next;
        }

        return frames;
    }

    /// <summary>Returns the response the vendor program would send for a device request, or null when none is needed.</summary>
    public static byte[]? BuildAcknowledgement(byte[] frame)
    {
        if (!IsFrame(frame) || frame[5] != Request)
        {
            return null;
        }

        return Command(frame) switch
        {
            "UCS" or "UCI" or "URR" or "UEC" => BuildFrame(Response, Command(frame)),
            "UII" => BuildFrame(Response, "UII", [1, 0, 0]),
            _ => null,
        };
    }

    public static bool IsResponseTo(byte[] frame, string command)
        => IsFrame(frame) && frame[5] == Response && Command(frame) == command;

    public static string Command(byte[] frame) => Encoding.ASCII.GetString(frame, 6, 3);

    /// <summary>Human-readable summary. Ticket numbers are masked (last 4 characters).</summary>
    public static string Describe(byte[] frame)
    {
        if (!IsFrame(frame))
        {
            return "알 수 없는 데이터";
        }

        var command = Command(frame);
        var body = Body(frame);
        var isRequest = frame[5] == Request;
        try
        {
            return command switch
            {
                "UDT" when !isRequest => "UDT 응답: 데이터 전송 " + (body.Length > 0 && body[0] == 1 ? "켜짐" : "꺼짐"),
                "UDT" => "UDT 요청: 데이터 전송 켜기",
                "CFG" => (isRequest ? "CFG 요청: " : "CFG 응답: ") + DescribeConfigure(body),
                "UCS" when isRequest => "계수 " + (body[2] == 1 ? "시작" : "종료"),
                "UCS" => "UCS 응답",
                "UCI" when isRequest => DescribeCount(body),
                "UCI" => "UCI 응답",
                "UTI" => DescribeTicket(body),
                "UII" when isRequest => $"전송 인덱스 {Read(body, 1, 2)}건 [{string.Join(", ", Enumerable.Range(0, Read(body, 1, 2)).Select(i => Read(body, 3 + (i * 2), 2)))}]",
                "UII" => "UII 응답",
                "UEC" when isRequest => $"오류 코드 {body[0]}",
                "URR" when isRequest => $"거부 사유 {string.Join(" ", body.Select(b => b.ToString("X2")))}",
                _ => $"{command} {(isRequest ? "요청" : "응답")} ({body.Length}B)",
            };
        }
        catch (IndexOutOfRangeException)
        {
            return $"{command} 해석 실패(데이터 길이 부족)";
        }
    }

    public static string MaskNumber(string number)
        => number.Length <= 4 ? new string('*', number.Length) : number[..^4] + "****";

    public static string TicketTypeName(int code) => code switch
    {
        2 => "삼성",
        3 => "롯데",
        4 => "홈플러스",
        5 => "이랜드",
        6 => "현대",
        7 => "현대 아이파크",
        8 => "코리아트래블",
        9 => "컬쳐랜드",
        _ => $"종류 코드 {code}",
    };

    private static string DescribeTicket(byte[] body)
    {
        var index = Read(body, 0, 2);
        var type = body[2];
        var amount = Read(body, 3, 3);
        var length = body[6];
        var number = Encoding.ASCII.GetString(body, 7, length);
        return $"상품권 #{index} | {TicketTypeName(type)} | {amount:N0}원 | 번호 {MaskNumber(number)}";
    }

    private static string DescribeCount(byte[] body)
    {
        var position = 3; // key(2) + mode(1)
        var currencies = body[position++];
        var parts = new List<string>();
        for (var i = 0; i < currencies; i++)
        {
            var name = Encoding.ASCII.GetString(body, position, 3);
            position += 3;
            var total = Read(body, position, 3);
            position += 3;
            var kinds = body[position++];
            var details = new List<string>();
            for (var k = 0; k < kinds; k++)
            {
                var value = Read(body, position, 3);
                var count = Read(body, position + 3, 3);
                position += 6;
                if (count > 0)
                {
                    details.Add($"{value:N0}원×{count}");
                }
            }

            parts.Add($"{name} 총 {total}장" + (details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty));
        }

        var reject = body[position];
        var doubled = body[position + 1];
        return $"집계 {string.Join(" / ", parts)} | 거부 {reject} | 이중 {doubled}";
    }

    private static string DescribeConfigure(byte[] body)
    {
        var enabled = ConfigureNames.Where((_, index) => index < body.Length && body[index] == 1).ToArray();
        return enabled.Length == 0 ? "전송 항목 없음" : string.Join(", ", enabled) + " 켜짐";
    }

    private static bool IsFrame(byte[] frame)
        => frame.Length >= 13 && frame.AsSpan(0, 5).SequenceEqual(StartMark);

    private static byte[] Body(byte[] frame)
    {
        // Everything between the 13-byte header and the "-EYE7" end mark.
        var end = IndexOf(frame, EndMark, 13);
        return end < 0 ? [] : frame[13..end];
    }

    private static int Read(byte[] data, int offset, int length)
    {
        var value = 0;
        for (var i = 0; i < length; i++)
        {
            value = (value << 8) | data[offset + i];
        }

        return value;
    }

    private static byte Xor(IEnumerable<byte> bytes) => bytes.Aggregate((byte)0, (acc, b) => (byte)(acc ^ b));

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        for (var i = start; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }
}
