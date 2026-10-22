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
    private bool _disposed;

    public MainViewModel()
        : this(new SerialPortService(), new SessionService())
    {
    }

    internal MainViewModel(SerialPortService serialPortService, SessionService sessionService)
    {
        _serialPortService = serialPortService;
        _sessionService = sessionService;
        _serialPortService.DataReceived += OnDataReceived;
        _serialPortService.Error += OnTransportError;
        _sessionService.Error += OnSessionError;

        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = [];

    public IReadOnlyList<int> BaudRates { get; } = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    public IReadOnlyList<int> DataBitsOptions { get; } = [7, 8];

    public IReadOnlyList<Parity> ParityOptions { get; } = [Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space];

    public IReadOnlyList<StopBits> StopBitsOptions { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];

    public IReadOnlyList<string> TxModes { get; } = ["ASCII", "HEX"];

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string? _selectedPort;

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

    public void PollPorts()
    {
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

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        try
        {
            var port = SelectedPort ?? _serialPortService.CurrentSettings?.PortName ?? string.Empty;
            _serialPortService.Disconnect();
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
            _serialPortService.Send(data);
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
        _sessionService.Error -= OnSessionError;

        try
        {
            _serialPortService.Disconnect();
        }
        finally
        {
            _serialPortService.Dispose();
            _sessionService.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private bool CanConnect() => !IsConnected && !string.IsNullOrWhiteSpace(SelectedPort);

    private bool CanDisconnect() => IsConnected;

    private bool CanStartSession() => !IsSessionActive;

    private bool CanStopSession() => IsSessionActive;

    private bool CanSend() => IsConnected && !string.IsNullOrWhiteSpace(TxText);

    private SerialSettings CreateSerialSettings()
    {
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
        Dispatcher.UIThread.Post(() => SetError("시리얼 포트 오류.", exception));
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
