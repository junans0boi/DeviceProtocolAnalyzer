namespace DeviceProtocolAnalyzer.Services;

/// <summary>
/// Decoder for the ETM coin sorter's serial output (115200 8N1), derived from USB captures and tests with known coin counts.
/// The device streams 13-byte frames "AA FF FF FF 00 00 00 00 d1 d2 d3 d4 00" while counting, one per coin,
/// where d1..d4 are the decimal digits of the running total in units of 10 won.
/// </summary>
public sealed class EtmProtocol
{
    public const int FrameLength = 13;

    private static readonly byte[] Header = [0xAA, 0xFF, 0xFF, 0xFF];

    private readonly List<byte> _buffer = [];
    private readonly SortedDictionary<int, int> _tally = [];
    private int? _previousTotal;

    /// <summary>Adds received bytes (any chunking) and returns the complete frames found so far.</summary>
    public IReadOnlyList<byte[]> Feed(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _buffer.AddRange(data);
        var frames = new List<byte[]>();
        while (true)
        {
            var start = IndexOfHeader();
            if (start < 0)
            {
                // Keep a possible partial header at the end.
                if (_buffer.Count > Header.Length - 1)
                {
                    _buffer.RemoveRange(0, _buffer.Count - (Header.Length - 1));
                }

                return frames;
            }

            if (start > 0)
            {
                _buffer.RemoveRange(0, start);
            }

            if (_buffer.Count < FrameLength)
            {
                return frames;
            }

            var frame = _buffer.GetRange(0, FrameLength).ToArray();
            if (IsValid(frame))
            {
                frames.Add(frame);
                _buffer.RemoveRange(0, FrameLength);
            }
            else
            {
                // Out of sync: skip one byte and look for the next header.
                _buffer.RemoveAt(0);
            }
        }
    }

    /// <summary>Running total in won (digits are in units of 10 won).</summary>
    public static int TotalWon(byte[] frame) => ((frame[8] * 1000) + (frame[9] * 100) + (frame[10] * 10) + frame[11]) * 10;

    /// <summary>
    /// Describes one frame relative to the previous one: the new total, which coin was added (by the increase) and,
    /// when the total returns to zero, a per-coin summary of the run.
    /// </summary>
    public string Describe(byte[] frame)
    {
        var total = TotalWon(frame);
        var previous = _previousTotal;
        _previousTotal = total;

        if (total == 0)
        {
            if (previous is null or 0)
            {
                _tally.Clear();
                return "리셋 (합계 0원)";
            }

            var summary = Summary(previous.Value);
            _tally.Clear();
            return $"리셋 | 직전 {summary}";
        }

        if (previous is null)
        {
            return $"합계 {total:N0}원";
        }

        var delta = total - previous.Value;
        if (delta == 0)
        {
            return $"합계 {total:N0}원 (변화 없음)";
        }

        if (delta < 0)
        {
            return $"합계 {total:N0}원 ({delta:N0}원, 감소)";
        }

        _tally[delta] = _tally.GetValueOrDefault(delta) + 1;
        return $"합계 {total:N0}원 (+{delta:N0}원 {CoinName(delta)})";
    }

    public void Reset()
    {
        _buffer.Clear();
        _tally.Clear();
        _previousTotal = null;
    }

    private string Summary(int total)
    {
        var parts = _tally.Select(entry => $"{CoinName(entry.Key)} {entry.Value}개").ToList();
        return (parts.Count == 0 ? "동전 없음" : string.Join(", ", parts)) + $" | 합계 {total:N0}원";
    }

    // The 10 won coin and the new 10 won coin (N10) add the same amount and cannot be told apart.
    private static string CoinName(int won) => won switch
    {
        10 => "10원(N10 포함)",
        50 => "50원",
        100 => "100원",
        500 => "500원",
        _ => "동전",
    };

    private static bool IsValid(byte[] frame)
    {
        for (var i = 4; i < 8; i++)
        {
            if (frame[i] != 0)
            {
                return false;
            }
        }

        for (var i = 8; i < 12; i++)
        {
            if (frame[i] > 9)
            {
                return false;
            }
        }

        return frame[12] == 0;
    }

    private int IndexOfHeader()
    {
        for (var i = 0; i <= _buffer.Count - Header.Length; i++)
        {
            if (_buffer[i] == Header[0] && _buffer[i + 1] == Header[1] && _buffer[i + 2] == Header[2] && _buffer[i + 3] == Header[3])
            {
                return i;
            }
        }

        return -1;
    }
}
