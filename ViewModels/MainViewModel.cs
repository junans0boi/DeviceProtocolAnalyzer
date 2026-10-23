using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeviceProtocolAnalyzer.Models;
using DeviceProtocolAnalyzer.Services;

namespace DeviceProtocolAnalyzer.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly SerialPortService _serialPortService;
    private readonly SessionService _sessionService;
    private readonly UsbDeviceService _usbDeviceService;
    private bool _disposed;

    public MainViewModel()
        : this(new SerialPortService(), new SessionService())
    {
    }

    internal MainViewModel(SerialPortService serialPortService, SessionService sessionService, UsbDeviceService? usbDeviceService = null)
    {
        _serialPortService = serialPortService;
        _sessionService = sessionService;
        _usbDeviceService = usbDeviceService ?? new UsbDeviceService();
        _serialPortService.DataReceived += OnDataReceived;
        _serialPortService.Error += OnTransportError;
        _usbDeviceService.DataReceived += OnDataReceived;
        _usbDeviceService.Error += OnTransportError;
        _sessionService.Error += OnSessionError;

        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = [];

    public IReadOnlyList<int> BaudRates { get; } = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    public IReadOnlyList<int> DataBitsOptions { get; } = [7, 8];

    public IReadOnlyList<Parity> ParityOptions { get; } = [Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space];

    public IReadOnlyList<StopBits> StopBitsOptions { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];

    public IReadOnlyList<string> TxModes { get; } = ["ASCII", "HEX"];

    public IReadOnlyList<string> TransportModes { get; } = [SerialMode, UsbMode];

    public ObservableCollection<UsbDeviceOption> AvailableUsbDevices { get; } = [];

    public ObservableCollection<UsbEndpointOption> UsbEndpoints { get; } = [];

    public ObservableCollection<UsbEndpointOption> UsbOutEndpoints { get; } = [];

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    private const string SerialMode = "시리얼";

    private const string UsbMode = "USB";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string? _selectedPort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSerialMode))]
    [NotifyPropertyChangedFor(nameof(IsUsbMode))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _selectedTransport = SerialMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private UsbDeviceOption? _selectedUsbDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private UsbEndpointOption? _selectedUsbEndpoint;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private UsbEndpointOption? _selectedUsbOutEndpoint;

    [ObservableProperty]
    private int _selectedBaudRate = 9600;

    [ObservableProperty]
    private int _selectedDataBits = 8;

    [ObservableProperty]
    private Parity _selectedParity = Parity.None;

    [ObservableProperty]
    private StopBits _selectedStopBits = StopBits.One;

    [ObservableProperty]
    private bool _dtrRtsEnable;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _testName = string.Empty;

    [ObservableProperty]
    private string _memo = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _txMode = "ASCII";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _txText = string.Empty;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopSessionCommand))]
    private bool _isSessionActive;

    [ObservableProperty]
    private long _rxByteCount;

    [ObservableProperty]
    private long _txByteCount;

    [ObservableProperty]
    private string _statusText = "연결 안 됨";

    [ObservableProperty]
    private string _lastError = string.Empty;

    [ObservableProperty]
    private string _portNotice = string.Empty;

    public bool IsSerialMode => SelectedTransport == SerialMode;

    public bool IsUsbMode => SelectedTransport == UsbMode;

    public string SessionDirectory => _sessionService.CurrentSession?.SessionDirectory ?? string.Empty;

    [RelayCommand]
    private void RefreshPorts()
    {
        try
        {
            var previousPort = SelectedPort;
            var ports = SerialPortService.GetPorts();

            AvailablePorts.Clear();
            foreach (var port in ports)
            {
                AvailablePorts.Add(port);
            }

            SelectedPort = previousPort is not null && AvailablePorts.Contains(previousPort)
                ? previousPort
                : AvailablePorts.FirstOrDefault();

            if (!IsConnected && AvailablePorts.Count == 0)
            {
                StatusText = "연결 안 됨 - 시리얼 포트 없음";
            }
        }
        catch (Exception exception)
        {
            SetError("시리얼 포트 목록을 가져올 수 없습니다.", exception);
        }
    }

    [RelayCommand]
    private void RefreshUsb()
    {
        try
        {
            ApplyUsbDevices(UsbDeviceService.GetDevices());
        }
        catch (Exception exception)
        {
            SetError("USB 장치 목록을 가져올 수 없습니다. libusb 라이브러리가 있는지 확인하세요.", exception);
        }
    }

    public void ApplyUsbDevices(IReadOnlyList<UsbDeviceOption> devices)
    {
        if (devices.SequenceEqual(AvailableUsbDevices))
        {
            return;
        }

        var previous = SelectedUsbDevice;
        var added = devices.Except(AvailableUsbDevices).ToList();
        var removed = AvailableUsbDevices.Except(devices).ToList();

        AvailableUsbDevices.Clear();
        foreach (var device in devices)
        {
            AvailableUsbDevices.Add(device);
        }

        // 연결 중에는 선택을 바꾸지 않는다.
        SelectedUsbDevice = IsConnected && previous is not null && AvailableUsbDevices.Contains(previous) ? previous
            : added.Count > 0 && !IsConnected ? added[^1]
            : previous is not null && AvailableUsbDevices.Contains(previous) ? previous
            : AvailableUsbDevices.FirstOrDefault();

        var notices = new List<string>();
        if (added.Count > 0)
        {
            notices.Add($"새 USB 장치 감지: {string.Join(", ", added.Select(device => $"{device.VendorId:X4}:{device.ProductId:X4}"))}");
        }

        if (removed.Count > 0)
        {
            notices.Add($"USB 장치 제거: {string.Join(", ", removed.Select(device => $"{device.VendorId:X4}:{device.ProductId:X4}"))}");
        }

        PortNotice = string.Join(" / ", notices);
    }

    partial void OnSelectedTransportChanged(string value)
    {
        if (value == UsbMode)
        {
            RefreshUsb();
        }
    }

    partial void OnSelectedUsbDeviceChanged(UsbDeviceOption? value)
    {
        UsbEndpoints.Clear();
        UsbOutEndpoints.Clear();
        SelectedUsbEndpoint = null;
        SelectedUsbOutEndpoint = null;
        if (value is null || IsConnected)
        {
            return;
        }

        try
        {
            foreach (var endpoint in UsbDeviceService.GetEndpoints(value))
            {
                (endpoint.IsIn ? UsbEndpoints : UsbOutEndpoints).Add(endpoint);
            }

            SelectedUsbEndpoint = UsbEndpoints.FirstOrDefault();
            SelectedUsbOutEndpoint = UsbOutEndpoints.FirstOrDefault();
            if (UsbEndpoints.Count == 0)
            {
                PortNotice = "이 장치에서 읽을 수 있는 IN 엔드포인트(Bulk/Interrupt)를 찾지 못했습니다.";
            }
        }
        catch (Exception exception)
        {
            SetError("USB 엔드포인트를 읽을 수 없습니다.", exception);
        }
    }

    public void PollPorts()
    {
        if (IsUsbMode)
        {
            try
            {
                ApplyUsbDevices(UsbDeviceService.GetDevices());
            }
            catch (Exception exception)
            {
                PortNotice = $"USB 장치 조회 실패: {exception.Message}";
            }

            return;
        }

        try
        {
            ApplyPorts(SerialPortService.GetPorts());
        }
        catch (Exception exception)
        {
            PortNotice = $"포트 목록 조회 실패: {exception.Message}";
        }
    }

    public void ApplyPorts(IReadOnlyList<string> ports)
    {
        var added = ports.Except(AvailablePorts).ToList();
        var removed = AvailablePorts.Except(ports).ToList();
        if (added.Count == 0 && removed.Count == 0)
        {
            return;
        }

        var previousPort = SelectedPort;
        AvailablePorts.Clear();
        foreach (var port in ports)
        {
            AvailablePorts.Add(port);
        }

        // 연결 중에는 선택을 바꾸지 않는다. 새로 꽂은 장비 포트를 자동 선택하는 것이 목적.
        SelectedPort = added.Count > 0 && !IsConnected ? added[^1]
            : previousPort is not null && AvailablePorts.Contains(previousPort) ? previousPort
            : AvailablePorts.FirstOrDefault();

        var notices = new List<string>();
        if (added.Count > 0)
        {
            notices.Add($"새 포트 감지: {string.Join(", ", added)}");
        }

        if (removed.Count > 0)
        {
            notices.Add($"포트 제거: {string.Join(", ", removed)}");
        }

        PortNotice = string.Join(" / ", notices);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect()
    {
        if (IsUsbMode)
        {
            ConnectUsb();
            return;
        }

        if (SelectedPort is null)
        {
            SetError("연결하기 전에 시리얼 포트를 선택하세요.");
            return;
        }

        var settings = CreateSerialSettings();
        try
        {
            _serialPortService.Connect(settings);
            IsConnected = true;
            LastError = string.Empty;
            StatusText = $"연결됨 - {settings.PortName}";
            _sessionService.WriteLog($"[{DateTimeOffset.Now:HH:mm:ss.fff}] CONNECT {settings.PortName}");
        }
        catch (Exception exception)
        {
            IsConnected = false;
            SetError($"{settings.PortName}에 연결할 수 없습니다.", exception);
            _sessionService.WriteLog($"[{DateTimeOffset.Now:HH:mm:ss.fff}] CONNECT ERROR {exception.Message}");
        }
    }

    private void ConnectUsb()
    {
        if (SelectedUsbDevice is null || SelectedUsbEndpoint is null)
        {
            SetError("연결하기 전에 USB 장치와 엔드포인트를 선택하세요.");
            return;
        }

        var name = CreateSerialSettings().PortName;
        try
        {
            _usbDeviceService.Connect(SelectedUsbDevice, SelectedUsbEndpoint, SelectedUsbOutEndpoint);
            IsConnected = true;
            LastError = string.Empty;
            StatusText = $"연결됨 - {name}";
            _sessionService.WriteLog($"[{DateTimeOffset.Now:HH:mm:ss.fff}] CONNECT {name}");
            _sessionService.WriteLog($"USB endpoints: {string.Join(" | ", UsbEndpoints.Concat(UsbOutEndpoints).Select(endpoint => endpoint.Display))}");
        }
        catch (Exception exception)
        {
            IsConnected = false;
            SetError($"{name}에 연결할 수 없습니다. Windows에서는 장치에 WinUSB 드라이버가 연결되어 있어야 합니다.", exception);
            _sessionService.WriteLog($"[{DateTimeOffset.Now:HH:mm:ss.fff}] CONNECT ERROR {exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        try
        {
            var port = IsUsbMode ? CreateSerialSettings().PortName : SelectedPort ?? _serialPortService.CurrentSettings?.PortName ?? string.Empty;
            _serialPortService.Disconnect();
            _usbDeviceService.Disconnect();
            IsConnected = false;
            StatusText = IsSessionActive ? "세션 진행 중 - 연결 안 됨" : "연결 안 됨";
            _sessionService.WriteLog($"[{DateTimeOffset.Now:HH:mm:ss.fff}] DISCONNECT {port}");
        }
        catch (Exception exception)
        {
            SetError("시리얼 포트 연결을 해제할 수 없습니다.", exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private void StartSession()
    {
        try
        {
            var session = new DeviceSession(
                Manufacturer,
                Model,
                TestName,
                Memo,
                CreateSerialSettings(),
                DateTimeOffset.Now);

            _sessionService.StartSession(session);
            IsSessionActive = true;
            LogEntries.Clear();
            RxByteCount = 0;
            TxByteCount = 0;
            LastError = string.Empty;
            StatusText = IsConnected ? $"연결됨 - {SelectedPort}" : "세션 진행 중 - 연결 안 됨";
            OnPropertyChanged(nameof(SessionDirectory));
        }
        catch (Exception exception)
        {
            SetError("세션을 시작할 수 없습니다.", exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopSession))]
    private async Task StopSessionAsync()
    {
        try
        {
            await _sessionService.StopSessionAsync($"[{DateTimeOffset.Now:HH:mm:ss.fff}] SESSION STOP");
            IsSessionActive = false;
            StatusText = IsConnected ? $"연결됨 - {SelectedPort}" : "연결 안 됨";
            OnPropertyChanged(nameof(SessionDirectory));
        }
        catch (Exception exception)
        {
            SetError("세션을 종료할 수 없습니다.", exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        byte[] data;
        try
        {
            data = string.Equals(TxMode, "HEX", StringComparison.OrdinalIgnoreCase)
                ? HexParser.Parse(TxText)
                : Encoding.ASCII.GetBytes(TxText);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            SetError("송신 데이터 형식이 올바르지 않습니다.", exception);
            return;
        }

        if (data.Length == 0)
        {
            SetError("송신 데이터가 비어 있습니다.");
            return;
        }

        try
        {
            if (IsUsbMode)
            {
                _usbDeviceService.Send(data);
            }
            else
            {
                _serialPortService.Send(data);
            }

            var timestamp = DateTimeOffset.Now;
            _sessionService.RecordTransmitted(data, timestamp);
            LogEntries.Add(LogEntry.Create(LogDirection.Tx, timestamp, data));
            TxByteCount += data.Length;
            LastError = string.Empty;
        }
        catch (Exception exception)
        {
            SetError("데이터를 송신할 수 없습니다.", exception);
            _sessionService.WriteLog($"[{DateTimeOffset.Now:HH:mm:ss.fff}] TX ERROR {exception.Message}");
        }
    }

    [RelayCommand]
    private void Clear()
    {
        LogEntries.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _serialPortService.DataReceived -= OnDataReceived;
        _serialPortService.Error -= OnTransportError;
        _usbDeviceService.DataReceived -= OnDataReceived;
        _usbDeviceService.Error -= OnTransportError;
        _sessionService.Error -= OnSessionError;

        try
        {
            _serialPortService.Disconnect();
            _usbDeviceService.Disconnect();
        }
        finally
        {
            _serialPortService.Dispose();
            _usbDeviceService.Dispose();
            _sessionService.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private bool CanConnect() => !IsConnected && (IsUsbMode
        ? SelectedUsbDevice is not null && SelectedUsbEndpoint is not null
        : !string.IsNullOrWhiteSpace(SelectedPort));

    private bool CanDisconnect() => IsConnected;

    private bool CanStartSession() => !IsSessionActive;

    private bool CanStopSession() => IsSessionActive;

    private bool CanSend() => IsConnected && (IsSerialMode || SelectedUsbOutEndpoint is not null) && !string.IsNullOrWhiteSpace(TxText);

    private SerialSettings CreateSerialSettings()
    {
        if (IsUsbMode)
        {
            var device = SelectedUsbDevice;
            var endpoint = SelectedUsbEndpoint;
            var name = device is null
                ? "USB"
                : $"USB {device.VendorId:X4}:{device.ProductId:X4}" + (endpoint is null ? string.Empty : $" EP 0x{endpoint.EndpointAddress:X2}");
            return new SerialSettings(name, 0, 0, Parity.None, StopBits.One, Transport: "usb");
        }

        return new SerialSettings(
            SelectedPort ?? string.Empty,
            SelectedBaudRate,
            SelectedDataBits,
            SelectedParity,
            SelectedStopBits,
            DtrRtsEnable);
    }

    private void OnDataReceived(object? sender, BytesReceivedEventArgs eventArgs)
    {
        var data = eventArgs.Data.ToArray();
        _sessionService.RecordReceived(data, eventArgs.Timestamp);

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            LogEntries.Add(LogEntry.Create(LogDirection.Rx, eventArgs.Timestamp, data));
            RxByteCount += data.Length;
        });
    }

    private void OnTransportError(Exception exception)
    {
        Dispatcher.UIThread.Post(() => SetError("장치 통신 오류.", exception));
    }

    private void OnSessionError(Exception exception)
    {
        Dispatcher.UIThread.Post(() => SetError("세션 로그 기록 오류.", exception));
    }

    private void SetError(string message, Exception? exception = null)
    {
        StatusText = "오류";
        LastError = exception is null ? message : $"{message} {exception.Message}";
    }
}
