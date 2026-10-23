using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeviceProtocolAnalyzer.Models;

namespace DeviceProtocolAnalyzer.Services;

public sealed class RawLogService : IDisposable
{
    private readonly string _rootDirectory;
    private readonly object _sync = new();
    private FileStream? _rawStream;
    private FileStream? _hexStream;
    private FileStream? _logStream;
    private StreamWriter? _hexWriter;
    private StreamWriter? _logWriter;

    public RawLogService(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : rootDirectory;
    }

    public bool IsActive { get; private set; }

    public void StartSession(DeviceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_sync)
        {
            if (IsActive)
            {
                throw new InvalidOperationException("A logging session is already active.");
            }

            var sessionDirectory = CreateSessionDirectory(session);
            try
            {
                _rawStream = new FileStream(
                    Path.Combine(sessionDirectory, "session.raw"),
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    options: FileOptions.WriteThrough);
                _hexStream = OpenTextStream(sessionDirectory, "session.hex");
                _logStream = OpenTextStream(sessionDirectory, "session.log");
                _hexWriter = CreateWriter(_hexStream);
                _logWriter = CreateWriter(_logStream);

                session.SessionDirectory = sessionDirectory;
                session.SessionId = Path.GetFileName(sessionDirectory);
                WriteMetadata(session);
                IsActive = true;
            }
            catch
            {
                CloseStreams();
                throw;
            }
        }
    }

    public void WriteReceived(byte[] data, DateTimeOffset timestamp)
    {
        WritePacket("RX", data, timestamp);
    }

    public void WriteTransmitted(byte[] data, DateTimeOffset timestamp)
    {
        WritePacket("TX", data, timestamp);
    }

    public void WriteRaw(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_sync)
        {
            EnsureActive();
            _rawStream!.Write(data, 0, data.Length);
            _rawStream.Flush(flushToDisk: true);
        }
    }

    public void WriteHex(byte[] data, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_sync)
        {
            EnsureActive();
            WriteHexCore(data, timestamp ?? DateTimeOffset.Now);
        }
    }

    public void WriteLog(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            EnsureActive();
            WriteLogCore(message);
        }
    }

    public void StopSession()
    {
        lock (_sync)
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            CloseStreams();
        }
    }

    public void Dispose()
    {
        StopSession();
        GC.SuppressFinalize(this);
    }

    private void WritePacket(string direction, byte[] data, DateTimeOffset timestamp)
    {
        WritePacket(direction, data, timestamp, writeRaw: direction == "RX");
    }

    private void WritePacket(string direction, byte[] data, DateTimeOffset timestamp, bool writeRaw)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_sync)
        {
            EnsureActive();
            if (writeRaw)
            {
                _rawStream!.Write(data, 0, data.Length);
                _rawStream.Flush(flushToDisk: true);
            }

            WriteHexCore(data, timestamp);
            WriteLogCore($"[{timestamp:HH:mm:ss.fff}] {direction} {data.Length:N0} bytes");
            WriteLogCore($"HEX   : {ByteFormatter.ToHex(data)}");
            WriteLogCore($"ASCII : {ByteFormatter.ToDisplayText(data)}");
            WriteLogCore(string.Empty);
        }
    }

    private void WriteHexCore(byte[] data, DateTimeOffset timestamp)
    {
        _hexWriter!.WriteLine($"[{timestamp:HH:mm:ss.fff}]");
        _hexWriter.WriteLine(ByteFormatter.ToHex(data));
        _hexWriter.WriteLine();
        _hexWriter.Flush();
        _hexStream!.Flush(flushToDisk: true);
    }

    private void WriteLogCore(string message)
    {
        _logWriter!.WriteLine(message);
        _logWriter.Flush();
        _logStream!.Flush(flushToDisk: true);
    }

    private void WriteMetadata(DeviceSession session)
    {
        var metadata = new
        {
            sessionId = session.SessionId,
            manufacturer = session.Manufacturer,
            model = session.Model,
            testName = session.TestName,
            memo = session.Memo,
            transport = session.SerialSettings.Transport,
            port = session.SerialSettings.PortName,
            baudRate = session.SerialSettings.BaudRate,
            dataBits = session.SerialSettings.DataBits,
            parity = session.SerialSettings.Parity.ToString(),
            stopBits = session.SerialSettings.StopBits.ToString(),
            startedAt = session.StartedAt,
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        File.WriteAllText(
            Path.Combine(session.SessionDirectory!, "metadata.json"),
            JsonSerializer.Serialize(metadata, options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string CreateSessionDirectory(DeviceSession session)
    {
        var dateDirectory = Path.Combine(_rootDirectory, session.StartedAt.LocalDateTime.ToString("yyyy-MM-dd"));
        var deviceDirectory = Path.Combine(
            dateDirectory,
            SanitizeSegment(string.IsNullOrWhiteSpace(session.Model) ? session.Manufacturer : session.Model));
        Directory.CreateDirectory(deviceDirectory);

        var timePrefix = session.StartedAt.LocalDateTime.ToString("HHmmss");
        for (var number = 1; number < 1000; number++)
        {
            var candidate = Path.Combine(deviceDirectory, $"{timePrefix}_session_{number:000}");
            if (!Directory.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
        }

        throw new IOException("Could not allocate a unique session directory.");
    }

    private static string SanitizeSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "UnknownDevice" : result;
    }

    private static FileStream OpenTextStream(string directory, string fileName)
    {
        return new FileStream(
            Path.Combine(directory, fileName),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
    }

    private static StreamWriter CreateWriter(FileStream stream)
    {
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true)
        {
            AutoFlush = true,
        };
    }

    private void EnsureActive()
    {
        if (!IsActive || _rawStream is null || _hexWriter is null || _logWriter is null)
        {
            throw new InvalidOperationException("No active logging session.");
        }
    }

    private void CloseStreams()
    {
        _hexWriter?.Dispose();
        _logWriter?.Dispose();
        _rawStream?.Dispose();
        _hexStream?.Dispose();
        _logStream?.Dispose();
        _hexWriter = null;
        _logWriter = null;
        _rawStream = null;
        _hexStream = null;
        _logStream = null;
    }
}
