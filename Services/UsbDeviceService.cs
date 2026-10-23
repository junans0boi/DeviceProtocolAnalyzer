using DeviceProtocolAnalyzer.Models;
using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace DeviceProtocolAnalyzer.Services;

/// <summary>
/// Reads raw bytes from a vendor-specific USB device through libusb (WinUSB on Windows).
/// libusb is only touched when listing or connecting, so the app still starts without the native library.
/// </summary>
public sealed class UsbDeviceService : IDisposable
{
    private readonly object _sync = new();
    private UsbContext? _context;
    private IUsbDevice? _device;
    private UsbEndpointWriter? _writer;
    private int _claimedInterface = -1;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;
    private bool _disposed;

    public event EventHandler<BytesReceivedEventArgs>? DataReceived;

    public event Action<Exception>? Error;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _device is not null;
            }
        }
    }

    public static IReadOnlyList<UsbDeviceOption> GetDevices()
    {
        using var context = new UsbContext();
        using var devices = context.List();
        return devices
            .OfType<UsbDevice>()
            .Select(device => new UsbDeviceOption(device.VendorId, device.ProductId, device.BusNumber, device.Address))
            .OrderBy(option => option.VendorId)
            .ThenBy(option => option.ProductId)
            .ToArray();
    }

    public static IReadOnlyList<UsbEndpointOption> GetEndpoints(UsbDeviceOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        using var context = new UsbContext();
        using var devices = context.List();
        var device = devices.FirstOrDefault(candidate => Matches(candidate, option))
            ?? throw new InvalidOperationException("선택한 USB 장치를 찾을 수 없습니다.");

        var endpoints = new List<UsbEndpointOption>();
        foreach (UsbConfigInfo config in device.Configs)
        {
            foreach (UsbInterfaceInfo usbInterface in config.Interfaces)
            {
                foreach (UsbEndpointInfo endpoint in usbInterface.Endpoints)
                {
                    var type = (endpoint.Attributes & 0x03) switch
                    {
                        2 => "Bulk",
                        3 => "Interrupt",
                        _ => null,
                    };

                    if (type is not null)
                    {
                        endpoints.Add(new UsbEndpointOption(usbInterface.Number, (byte)usbInterface.Class, endpoint.EndpointAddress, type, endpoint.MaxPacketSize));
                    }
                }
            }
        }

        return endpoints;
    }

    public void Connect(UsbDeviceOption deviceOption, UsbEndpointOption endpointOption, UsbEndpointOption? outEndpoint = null)
    {
        ArgumentNullException.ThrowIfNull(deviceOption);
        ArgumentNullException.ThrowIfNull(endpointOption);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DisconnectCore();

            var context = new UsbContext();
            IUsbDevice? device = null;
            var claimed = -1;
            try
            {
                device = context.List().FirstOrDefault(candidate => Matches(candidate, deviceOption))
                    ?? throw new InvalidOperationException("선택한 USB 장치를 찾을 수 없습니다.");

                device.Open();
                device.ClaimInterface(endpointOption.InterfaceNumber);
                claimed = endpointOption.InterfaceNumber;

                var reader = device.OpenEndpointReader((ReadEndpointID)endpointOption.EndpointAddress);
                UsbEndpointWriter? writer = null;
                if (outEndpoint is not null)
                {
                    if (outEndpoint.InterfaceNumber != claimed)
                    {
                        device.ClaimInterface(outEndpoint.InterfaceNumber);
                    }

                    writer = device.OpenEndpointWriter((WriteEndpointID)outEndpoint.EndpointAddress);
                }

                var cancellation = new CancellationTokenSource();

                _context = context;
                _device = device;
                _writer = writer;
                _claimedInterface = claimed;
                _readCancellation = cancellation;
                _readTask = Task.Run(() => ReadLoop(reader, Math.Max(endpointOption.MaxPacketSize, 64), cancellation.Token));
            }
            catch
            {
                if (claimed >= 0)
                {
                    TryRun(() => device!.ReleaseInterface(claimed));
                }

                TryRun(() => device?.Close());
                TryRun(() => device?.Dispose());
                context.Dispose();
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
            ObjectDisposedException.ThrowIf(_disposed, this);
            var writer = _writer ?? throw new InvalidOperationException("송신 엔드포인트(OUT)가 선택되지 않았습니다.");
            var result = writer.Write(data, 1000, out _);
            if (result != LibUsbDotNet.Error.Success)
            {
                throw new IOException($"USB 쓰기 실패: {result}");
            }
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

    private static bool Matches(IUsbDevice candidate, UsbDeviceOption option)
    {
        return candidate is UsbDevice device
            && device.VendorId == option.VendorId
            && device.ProductId == option.ProductId
            && device.BusNumber == option.Bus
            && device.Address == option.Address;
    }

    private void ReadLoop(UsbEndpointReader reader, int packetSize, CancellationToken token)
    {
        var buffer = new byte[Math.Max(packetSize * 8, 4096)];
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = reader.Read(buffer, 250, out var length);
                if (result == LibUsbDotNet.Error.Timeout)
                {
                    continue;
                }

                if (result != LibUsbDotNet.Error.Success)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Error?.Invoke(new IOException($"USB 읽기 실패: {result}"));
                    }

                    return;
                }

                if (length > 0)
                {
                    DataReceived?.Invoke(this, new BytesReceivedEventArgs(buffer.AsSpan(0, length).ToArray(), DateTimeOffset.Now));
                }
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested)
                {
                    Error?.Invoke(exception);
                }

                return;
            }
        }
    }

    private void DisconnectCore()
    {
        var cancellation = _readCancellation;
        var readTask = _readTask;
        var device = _device;
        var context = _context;
        var claimed = _claimedInterface;

        _writer = null;
        _readCancellation = null;
        _readTask = null;
        _device = null;
        _context = null;
        _claimedInterface = -1;

        cancellation?.Cancel();
        TryRun(() => readTask?.Wait(TimeSpan.FromSeconds(1)));

        if (claimed >= 0)
        {
            TryRun(() => device?.ReleaseInterface(claimed));
        }

        TryRun(() => device?.Close());
        TryRun(() => device?.Dispose());
        TryRun(() => context?.Dispose());
        cancellation?.Dispose();
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Teardown is best effort; the device may already be unplugged.
        }
    }
}
