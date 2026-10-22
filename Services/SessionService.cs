using System.Threading.Channels;
using DeviceProtocolAnalyzer.Models;

namespace DeviceProtocolAnalyzer.Services;

public sealed class SessionService : IAsyncDisposable, IDisposable
{
    private readonly RawLogService _rawLogService;
    private readonly Channel<SessionWorkItem> _queue = Channel.CreateUnbounded<SessionWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
    private readonly CancellationTokenSource _workerCancellation = new();
    private readonly object _sync = new();
    private readonly Task _workerTask;
    private DeviceSession? _activeSession;
    private bool _accepting;
    private bool _disposed;

    public SessionService(RawLogService? rawLogService = null)
    {
        _rawLogService = rawLogService ?? new RawLogService();
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public event Action<Exception>? Error;

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _accepting && _activeSession is not null;
            }
        }
    }

    public DeviceSession? CurrentSession
    {
        get
        {
            lock (_sync)
            {
                return _activeSession;
            }
        }
    }

    public void StartSession(DeviceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_activeSession is not null)
            {
                throw new InvalidOperationException("A session is already active.");
            }

            _rawLogService.StartSession(session);
            _activeSession = session;
            _accepting = true;
            EnqueueLogCore($"[{session.StartedAt:HH:mm:ss.fff}] SESSION START");
        }
    }

    public void RecordReceived(byte[] data, DateTimeOffset timestamp)
    {
        EnqueuePacket(LogDirection.Rx, data, timestamp);
    }

    public void RecordTransmitted(byte[] data, DateTimeOffset timestamp)
    {
        EnqueuePacket(LogDirection.Tx, data, timestamp);
    }

    public void WriteLog(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            if (_accepting && _activeSession is not null)
            {
                EnqueueLogCore(message);
            }
        }
    }

    public async Task StopSessionAsync(string? finalMessage = null)
    {
        DeviceSession? session;
        TaskCompletionSource<bool>? flushed = null;

        lock (_sync)
        {
            if (_activeSession is null)
            {
                return;
            }

            session = _activeSession;
            _accepting = false;

            if (!string.IsNullOrWhiteSpace(finalMessage))
            {
                EnqueueLogCore(finalMessage);
            }

            flushed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Writer.TryWrite(SessionWorkItem.Flush(session, flushed));
        }

        await flushed.Task.ConfigureAwait(false);

        lock (_sync)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
                _rawLogService.StopSession();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopSessionAsync("SESSION STOP").ConfigureAwait(false);
        _disposed = true;
        _queue.Writer.TryComplete();
        _workerCancellation.Cancel();

        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _workerCancellation.Dispose();
            _rawLogService.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void EnqueuePacket(LogDirection direction, byte[] data, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_sync)
        {
            if (_accepting && _activeSession is not null)
            {
                _queue.Writer.TryWrite(SessionWorkItem.Packet(_activeSession, direction, data.ToArray(), timestamp));
            }
        }
    }

    private void EnqueueLogCore(string message)
    {
        _queue.Writer.TryWrite(SessionWorkItem.Log(_activeSession!, message));
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_workerCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    if (item.Kind == SessionWorkKind.Flush)
                    {
                        item.Flushed!.TrySetResult(true);
                        continue;
                    }

                    if (!IsCurrentSession(item.Session))
                    {
                        continue;
                    }

                    switch (item.Kind)
                    {
                        case SessionWorkKind.Packet when item.Direction == LogDirection.Rx:
                            _rawLogService.WriteReceived(item.Data!, item.Timestamp);
                            break;
                        case SessionWorkKind.Packet:
                            _rawLogService.WriteTransmitted(item.Data!, item.Timestamp);
                            break;
                        case SessionWorkKind.Log:
                            _rawLogService.WriteLog(item.Message!);
                            break;
                    }
                }
                catch (Exception exception)
                {
                    Error?.Invoke(exception);
                    item.Flushed?.TrySetException(exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsCurrentSession(DeviceSession session)
    {
        lock (_sync)
        {
            return ReferenceEquals(_activeSession, session);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SessionService));
        }
    }

    private enum SessionWorkKind
    {
        Packet,
        Log,
        Flush,
    }

    private sealed record SessionWorkItem(
        SessionWorkKind Kind,
        DeviceSession Session,
        LogDirection? Direction,
        byte[]? Data,
        DateTimeOffset Timestamp,
        string? Message,
        TaskCompletionSource<bool>? Flushed)
    {
        public static SessionWorkItem Packet(DeviceSession session, LogDirection direction, byte[] data, DateTimeOffset timestamp)
            => new(SessionWorkKind.Packet, session, direction, data, timestamp, null, null);

        public static SessionWorkItem Log(DeviceSession session, string message)
            => new(SessionWorkKind.Log, session, null, null, DateTimeOffset.Now, message, null);

        public static SessionWorkItem Flush(DeviceSession session, TaskCompletionSource<bool> flushed)
            => new(SessionWorkKind.Flush, session, null, null, DateTimeOffset.Now, null, flushed);
    }
}
