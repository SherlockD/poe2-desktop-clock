using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.Models;

namespace Poe2DesktopClock.Desktop.Services;

/// <summary>
/// Adapts application use cases into one coherent, periodically refreshed
/// monitoring state for the WPF dashboard.
/// </summary>
public sealed class RuntimeTrackerStatusProvider : ITrackerStatusProvider, IAsyncDisposable
{
    private static readonly GameSessionSnapshot GameNotRunning = new(
        GameSessionStatus.GameNotRunning,
        null,
        null,
        null,
        null,
        null,
        null);

    private readonly ITrackerRefreshUseCase _refresh;
    private readonly ITrackerMonitoringUseCase _monitoring;
    private readonly IGameSessionUseCase _session;
    private readonly IDeviceSynchronizationUseCase _device;
    private readonly DeviceSnapshotRelay _deviceRelay;
    private readonly object _stateSync = new();
    private ClockSnapshot? _clockSnapshot;
    private ClockMonitorStatus _monitorStatus = ClockMonitorStatus.Stopped;
    private GameStatus _gameStatus = new(false, "Path of Exile 2 не запущен.");
    private GameSessionSnapshot _sessionSnapshot = GameNotRunning;
    private DeviceSynchronizationState _deviceState;
    private CancellationTokenSource? _sessionPollingCancellation;
    private Task? _sessionPollingTask;
    private bool _initialized;
    private bool _disposed;

    public RuntimeTrackerStatusProvider(
        ITrackerRefreshUseCase refresh,
        ITrackerMonitoringUseCase monitoring,
        IGameSessionUseCase session,
        IDeviceSynchronizationUseCase device,
        ILastClockSnapshotStore lastSnapshots,
        DeviceSnapshotRelay deviceRelay)
    {
        _refresh = refresh;
        _monitoring = monitoring;
        _session = session;
        _device = device;
        _deviceRelay = deviceRelay;
        _clockSnapshot = lastSnapshots.GetLastSnapshot();
        _deviceState = device.CurrentState;
        _refresh.ClockSnapshotChanged += OnClockSnapshotChanged;
        _monitoring.MonitorStatusChanged += OnMonitorStatusChanged;
        _device.SynchronizationStateChanged += OnDeviceSynchronizationStateChanged;
    }

    public event EventHandler<TrackerStatusSnapshot>? StatusChanged;

    public TrackerStatusSnapshot GetCurrent()
    {
        lock (_stateSync)
        {
            return new TrackerStatusSnapshot(
                _clockSnapshot,
                _gameStatus,
                _monitorStatus,
                _sessionSnapshot,
                _deviceState);
        }
    }

    public async Task InitializeAsync()
    {
        ClockSnapshot? persistedSnapshot;
        lock (_stateSync)
        {
            if (_initialized || _disposed)
            {
                return;
            }

            _initialized = true;
            persistedSnapshot = _clockSnapshot;
        }

        RefreshGameSession();
        await _monitoring.StartCurrencyMonitoringAsync();
        StartSessionPolling();

        if (persistedSnapshot is not null)
        {
            _deviceRelay.QueueSnapshot(persistedSnapshot);
        }

        PublishCurrent();
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cancellation;
        Task? pollingTask;
        lock (_stateSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _sessionPollingCancellation;
            pollingTask = _sessionPollingTask;
            _sessionPollingCancellation = null;
            _sessionPollingTask = null;
        }

        _refresh.ClockSnapshotChanged -= OnClockSnapshotChanged;
        _monitoring.MonitorStatusChanged -= OnMonitorStatusChanged;
        _device.SynchronizationStateChanged -= OnDeviceSynchronizationStateChanged;
        cancellation?.Cancel();
        try
        {
            if (pollingTask is not null)
            {
                await pollingTask;
            }
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            // Expected shutdown path.
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    private void OnClockSnapshotChanged(object? sender, ClockSnapshot snapshot)
    {
        var session = _session.UpdateClockSnapshot(snapshot);
        lock (_stateSync)
        {
            _clockSnapshot = snapshot;
            _sessionSnapshot = session;
        }

        PublishCurrent();
    }

    private void OnMonitorStatusChanged(object? sender, ClockMonitorStatus status)
    {
        lock (_stateSync)
        {
            _monitorStatus = status;
        }

        PublishCurrent();
    }

    private void OnDeviceSynchronizationStateChanged(object? sender, DeviceSynchronizationState state)
    {
        lock (_stateSync)
        {
            _deviceState = state;
        }

        PublishCurrent();
    }

    private void StartSessionPolling()
    {
        lock (_stateSync)
        {
            if (_disposed || _sessionPollingCancellation is not null)
            {
                return;
            }

            _sessionPollingCancellation = new CancellationTokenSource();
            _sessionPollingTask = PollGameSessionAsync(_sessionPollingCancellation.Token);
        }
    }

    private async Task PollGameSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                RefreshGameSession();
                PublishCurrent();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected shutdown path.
        }
    }

    private void RefreshGameSession()
    {
        var gameStatus = _monitoring.GetGameStatus();
        var session = _session.UpdateGameStatus(gameStatus);
        lock (_stateSync)
        {
            _gameStatus = gameStatus;
            _sessionSnapshot = session;
        }
    }

    private void PublishCurrent() => StatusChanged?.Invoke(this, GetCurrent());
}
