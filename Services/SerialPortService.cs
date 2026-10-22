using System.IO.Ports;
using DeviceProtocolAnalyzer.Models;

namespace DeviceProtocolAnalyzer.Services;

public sealed class BytesReceivedEventArgs : EventArgs
{
    public BytesReceivedEventArgs(byte[] data, DateTimeOffset timestamp)
    {
        Data = data;
        Timestamp = timestamp;
    }

    public byte[] Data { get; }

    public DateTimeOffset Timestamp { get; }
}

public sealed class SerialPortService : IDisposable
{
    private readonly object _sync = new();
    private SerialPort? _serialPort;
    private bool _disposed;

    public event EventHandler<BytesReceivedEventArgs>? DataReceived;

    public event Action<Exception>? Error;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _serialPort?.IsOpen == true;
            }
        }
    }

    public SerialSettings? CurrentSettings { get; private set; }

    public static IReadOnlyList<string> GetPorts()
    {
        return SerialPort.GetPortNames()
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Connect(SerialSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.PortName))
        {
            throw new ArgumentException("A serial port must be selected.", nameof(settings));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            DisconnectCore();

            var port = new SerialPort(
                settings.PortName,
                settings.BaudRate,
                settings.Parity,
                settings.DataBits,
                settings.StopBits)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = settings.DtrRtsEnable,
                RtsEnable = settings.DtrRtsEnable,
            };

            port.DataReceived += OnDataReceived;
            try
            {
                port.Open();
                _serialPort = port;
                CurrentSettings = settings;
            }
            catch
            {
                port.DataReceived -= OnDataReceived;
                port.Dispose();
                throw;
            }
        }
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            DisconnectCore();
        }
    }

    public void Send(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_serialPort is not { IsOpen: true } port)
            {
                throw new InvalidOperationException("The serial port is not connected.");
            }

            port.Write(data, 0, data.Length);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            DisconnectCore();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        SerialPort? port;
        lock (_sync)
        {
            port = _serialPort;
        }

        if (port is null || !port.IsOpen)
        {
            return;
        }

        try
        {
            var available = port.BytesToRead;
            if (available <= 0)
            {
                return;
            }

            var buffer = new byte[available];
            var read = port.Read(buffer, 0, buffer.Length);
            if (read != buffer.Length)
            {
                Array.Resize(ref buffer, read);
            }

            if (buffer.Length > 0)
            {
                DataReceived?.Invoke(this, new BytesReceivedEventArgs(buffer, DateTimeOffset.Now));
            }
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_serialPort, port))
                {
                    return;
                }
            }

            Error?.Invoke(exception);
        }
    }

    private void DisconnectCore()
    {
        var port = _serialPort;
        _serialPort = null;
        CurrentSettings = null;

        if (port is null)
        {
            return;
        }

        port.DataReceived -= OnDataReceived;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        finally
        {
            port.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SerialPortService));
        }
    }
}
