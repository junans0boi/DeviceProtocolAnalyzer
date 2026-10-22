using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Models;

public enum LogDirection
{
    Rx,
    Tx,
}

public sealed class LogEntry
{
    private LogEntry(LogDirection direction, DateTimeOffset timestamp, byte[] data)
    {
        Direction = direction;
        Timestamp = timestamp;
        Data = data;
        Hex = ByteFormatter.ToHex(data);
        Ascii = ByteFormatter.ToDisplayText(data);
    }

    public LogDirection Direction { get; }

    public DateTimeOffset Timestamp { get; }

    public byte[] Data { get; }

    public string Hex { get; }

    public string Ascii { get; }

    public string Header => $"[{Timestamp:HH:mm:ss.fff}] {Direction.ToString().ToUpperInvariant()} {Data.Length:N0} bytes";

    public string HexLine => $"HEX   : {Hex}";

    public string AsciiLine => $"ASCII : {Ascii}";

    public static LogEntry Create(LogDirection direction, DateTimeOffset timestamp, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new LogEntry(direction, timestamp, data.ToArray());
    }
}
