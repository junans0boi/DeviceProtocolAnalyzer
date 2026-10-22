using System.Text.Json;
using DeviceProtocolAnalyzer.Models;
using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.Tests;

public sealed class RawLogServiceTests
{
    [Fact]
    public void StartAndWriteSessionPersistsExactBytesAndMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeviceProtocolAnalyzerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var settings = new SerialSettings("COM4", 9600, 8, System.IO.Ports.Parity.None, System.IO.Ports.StopBits.One);
            var session = new DeviceSession("Kisan", "K6", "sample", "50,000원 x 1", settings, DateTimeOffset.Now);
            var bytes = new byte[] { 0x02, 0x4B, 0x36, 0x03 };
            var service = new RawLogService(root);

            service.StartSession(session);
            service.WriteReceived(bytes, DateTimeOffset.Parse("2026-10-16T14:04:02.451+09:00"));
            service.WriteTransmitted(new byte[] { 0x10, 0x11 }, DateTimeOffset.Parse("2026-10-16T14:04:03.451+09:00"));
            service.StopSession();

            Assert.NotNull(session.SessionDirectory);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(session.SessionDirectory!, "session.raw")));
            Assert.Contains("02 4B 36 03", File.ReadAllText(Path.Combine(session.SessionDirectory!, "session.hex")));
            Assert.Contains("10 11", File.ReadAllText(Path.Combine(session.SessionDirectory!, "session.hex")));
            Assert.Contains("RX 4 bytes", File.ReadAllText(Path.Combine(session.SessionDirectory!, "session.log")));

            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(session.SessionDirectory!, "metadata.json")));
            Assert.Equal("K6", metadata.RootElement.GetProperty("model").GetString());
            Assert.Equal(9600, metadata.RootElement.GetProperty("baudRate").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SessionServiceFlushesQueuedPacketsBeforeStopping()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeviceProtocolAnalyzerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var settings = new SerialSettings("COM4", 9600, 8, System.IO.Ports.Parity.None, System.IO.Ports.StopBits.One);
            var session = new DeviceSession("Kisan", "K6", "queued", string.Empty, settings, DateTimeOffset.Now);
            await using var service = new SessionService(new RawLogService(root));
            service.StartSession(session);

            service.RecordReceived(new byte[] { 0x01, 0x02 }, DateTimeOffset.Now);
            service.RecordTransmitted(new byte[] { 0x09 }, DateTimeOffset.Now);
            await service.StopSessionAsync();

            Assert.Equal(new byte[] { 0x01, 0x02 }, File.ReadAllBytes(Path.Combine(session.SessionDirectory!, "session.raw")));
            var log = File.ReadAllText(Path.Combine(session.SessionDirectory!, "session.log"));
            Assert.Contains("RX 2 bytes", log);
            Assert.Contains("TX 1 bytes", log);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
