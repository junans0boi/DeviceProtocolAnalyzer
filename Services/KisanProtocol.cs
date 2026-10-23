namespace DeviceProtocolAnalyzer.Services;

/// <summary>
/// Decoder for the Kisan banknote counter's serial output (115200 8N1), derived from USB captures of the vendor
/// program and tests with known counts. The device sends frames "EA cmd payload EE" on its own; nothing is sent to it.
/// </summary>
public sealed class KisanProtocol
{
    private const byte Start = 0xEA;

    private const byte End = 0xEE;

    private const int SummaryPayloadLength = 44;

    // Total frame length (including EA and EE) per command byte.
    private static readonly Dictionary<byte, int> FrameLengths = new()
    {
        [0x01] = 6,   // one banknote counted
        [0x02] = 5,   // banknote rejected
        [0x04] = 3,   // signal (reject cleared / reset sequence)
        [0x05] = 3,   // signal (reject cleared / reset sequence)
        [0x0C] = 2 + SummaryPayloadLength + 1, // summary
        [0x0E] = 3,   // status signal after reset
    };

    private static readonly int[] DenominationCodes = [1000, 5000, 10000, 50000];

    private readonly List<byte> _buffer = [];

    /// <summary>Adds received bytes (any chunking) and returns the complete frames found so far.</summary>
    public IReadOnlyList<byte[]> Feed(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _buffer.AddRange(data);
        var frames = new List<byte[]>();
        while (_buffer.Count > 0)
        {
            if (_buffer[0] != Start)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            if (_buffer.Count < 2)
            {
                break;
            }

            if (!FrameLengths.TryGetValue(_buffer[1], out var length))
            {
                _buffer.RemoveAt(0);
                continue;
            }

            if (_buffer.Count < length)
            {
                break;
            }

            if (_buffer[length - 1] != End)
            {
                // Not a real frame start: resynchronise on the next EA.
                _buffer.RemoveAt(0);
                continue;
            }

            frames.Add(_buffer.GetRange(0, length).ToArray());
            _buffer.RemoveRange(0, length);
        }

        return frames;
    }

    public void Reset() => _buffer.Clear();

    public static string Describe(byte[] frame)
    {
        if (frame.Length < 3 || frame[0] != Start || frame[^1] != End)
        {
            return "알 수 없는 데이터";
        }

        var command = frame[1];
        var payload = frame[2..^1];
        try
        {
            return command switch
            {
                0x01 => $"지폐 {BillName(payload[2])}",
                0x02 => $"거부 1장 (사유 {payload[0]})",
                0x04 => "신호 04 (거부 해제/리셋 시퀀스)",
                0x05 => "신호 05 (거부 해제/리셋 시퀀스)",
                0x0E => "신호 0E (리셋 후 상태)",
                0x0C => DescribeSummary(payload),
                _ => $"명령 0x{command:X2} ({payload.Length}B)",
            };
        }
        catch (IndexOutOfRangeException)
        {
            return $"명령 0x{command:X2} 해석 실패(데이터 길이 부족)";
        }
    }

    /// <summary>Banknote code in the bill frame: 4 = 1,000, 8 = 5,000, 12 = 10,000, 16 = 50,000 won.</summary>
    public static string BillName(byte code)
    {
        var index = (code / 4) - 1;
        return code % 4 == 0 && index >= 0 && index < DenominationCodes.Length
            ? $"{DenominationCodes[index]:N0}원"
            : $"(코드 0x{code:X2})";
    }

    /// <summary>Checksum of the summary payload: 0x7F xor every byte before it.</summary>
    public static bool SummaryChecksumValid(byte[] payload)
    {
        if (payload.Length != SummaryPayloadLength)
        {
            return false;
        }

        var check = (byte)0x7F;
        for (var i = 0; i < 40; i++)
        {
            check ^= payload[i];
        }

        return check == payload[40];
    }

    private static string DescribeSummary(byte[] payload)
    {
        var counts = new int[DenominationCodes.Length];
        for (var i = 0; i < counts.Length; i++)
        {
            counts[i] = ReadLe(payload, i * 2, 2);
        }

        var total = ReadLe(payload, 36, 4);
        var rejects = ReadLe(payload, 32, 2);
        var sheets = ReadLe(payload, 30, 2);
        var valid = SummaryChecksumValid(payload) ? string.Empty : " (체크 불일치)";

        if (counts.All(count => count == 0) && total == 0 && rejects == 0)
        {
            return "집계 초기화 (0장)" + valid;
        }

        var parts = counts
            .Select((count, index) => (count, index))
            .Where(entry => entry.count > 0)
            .Select(entry => $"{DenominationCodes[entry.index]:N0}원 {entry.count}장");
        var rejectText = rejects > 0 ? $" | 거부 {rejects}장" : string.Empty;
        return $"집계 {string.Join(", ", parts)} | {sheets}장{rejectText} | 합계 {total:N0}원{valid}";
    }

    private static int ReadLe(byte[] data, int offset, int length)
    {
        var value = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            value = (value << 8) | data[offset + i];
        }

        return value;
    }
}
