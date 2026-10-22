namespace DeviceProtocolAnalyzer.Models;

public sealed class DeviceSession
{
    public DeviceSession(
        string manufacturer,
        string model,
        string testName,
        string memo,
        SerialSettings serialSettings,
        DateTimeOffset startedAt)
    {
        Manufacturer = manufacturer.Trim();
        Model = model.Trim();
        TestName = testName.Trim();
        Memo = memo.Trim();
        SerialSettings = serialSettings;
        StartedAt = startedAt;
    }

    public string Manufacturer { get; }

    public string Model { get; }

    public string TestName { get; }

    public string Memo { get; }

    public SerialSettings SerialSettings { get; }

    public DateTimeOffset StartedAt { get; }

    public string? SessionId { get; internal set; }

    public string? SessionDirectory { get; internal set; }
}
