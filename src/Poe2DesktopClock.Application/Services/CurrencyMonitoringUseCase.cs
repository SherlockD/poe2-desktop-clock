using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>Owns monitoring lifetime and requests an application refresh on a detected change.</summary>
public sealed class CurrencyMonitoringUseCase : ITrackerMonitoringUseCase, IAsyncDisposable
{
    private readonly ITrackerSettingsUseCase _settings;
    private readonly ITrackerRefreshUseCase _refresh;
    private readonly ICurrencyChangeMonitor _monitor;
    private readonly IGameStatusReader _gameStatus;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private Task? _automaticRefreshTask;
    private Task? _currencyRefreshTask;
    private CurrencyTabFrame? _pendingCurrencyFrame;

    public CurrencyMonitoringUseCase(
        ITrackerSettingsUseCase settings,
        ITrackerRefreshUseCase refresh,
        ICurrencyChangeMonitor monitor,
        IGameStatusReader gameStatus)
    {
        _settings = settings;
        _refresh = refresh;
        _monitor = monitor;
        _gameStatus = gameStatus;
        _monitor.CurrencyChanged += OnCurrencyChanged;
        _monitor.StatusChanged += OnStatusChanged;
    }

    public event EventHandler<ClockMonitorStatus>? MonitorStatusChanged;

    public GameStatus GetGameStatus() => _gameStatus.GetGameStatus();

    public async Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            lock (_lifecycleSync)
            {
                if (_cancellation is not null)
                {
                    return;
                }

                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var settings = _settings.GetSettings();
                _runTask = settings.IsCurrencyMonitoringEnabled
                    ? _monitor.RunAsync(TimeSpan.FromSeconds(1d / settings.CurrencyScreensPerSecond), _cancellation.Token)
                    : Task.CompletedTask;
                _automaticRefreshTask = RefreshPublicTabsPeriodicallyAsync(
                    TimeSpan.FromMinutes(settings.PublicRefreshIntervalMinutes),
                    _cancellation.Token);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopCurrencyMonitoringAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            CancellationTokenSource? cancellation;
            Task? runTask;
            Task? automaticRefreshTask;
            Task? currencyRefreshTask;
            lock (_lifecycleSync)
            {
                cancellation = _cancellation;
                if (cancellation is null)
                {
                    return;
                }

                _cancellation = null;
                runTask = _runTask;
                automaticRefreshTask = _automaticRefreshTask;
                currencyRefreshTask = _currencyRefreshTask;
                _runTask = null;
                _automaticRefreshTask = null;
                _currencyRefreshTask = null;
                _pendingCurrencyFrame = null;
            }

            cancellation.Cancel();
            try
            {
                await Task.WhenAll(
                    runTask ?? Task.CompletedTask,
                    automaticRefreshTask ?? Task.CompletedTask,
                    currencyRefreshTask ?? Task.CompletedTask);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Expected shutdown path.
            }
            finally
            {
                cancellation.Dispose();
                MonitorStatusChanged?.Invoke(this, ClockMonitorStatus.Stopped);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopCurrencyMonitoringAsync();
        _monitor.CurrencyChanged -= OnCurrencyChanged;
        _monitor.StatusChanged -= OnStatusChanged;
        _lifecycleGate.Dispose();
    }

    private void OnStatusChanged(object? sender, ClockMonitorStatus status) =>
        MonitorStatusChanged?.Invoke(this, status);

    private void OnCurrencyChanged(object? sender, CurrencyTabChangedEventArgs eventArgs)
    {
        lock (_lifecycleSync)
        {
            if (_cancellation is null ||
                _cancellation.IsCancellationRequested)
            {
                return;
            }

            _pendingCurrencyFrame = eventArgs.Frame;
            if (_currencyRefreshTask is not { IsCompleted: false })
            {
                _currencyRefreshTask = RefreshCurrencyFramesAsync(_cancellation.Token);
            }
        }
    }

    private async Task RefreshCurrencyFramesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            CurrencyTabFrame? frame;
            lock (_lifecycleSync)
            {
                if (cancellationToken.IsCancellationRequested || _cancellation is null)
                {
                    _pendingCurrencyFrame = null;
                    _currencyRefreshTask = null;
                    return;
                }

                frame = _pendingCurrencyFrame;
                _pendingCurrencyFrame = null;
                if (frame is null)
                {
                    _currencyRefreshTask = null;
                    return;
                }
            }

            try
            {
                await _refresh.RefreshCurrencyAsync(frame, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_lifecycleSync)
                {
                    _pendingCurrencyFrame = null;
                    _currencyRefreshTask = null;
                }

                return;
            }
            catch
            {
                MonitorStatusChanged?.Invoke(this, ClockMonitorStatus.Error);
            }
        }
    }

    private async Task RefreshPublicTabsPeriodicallyAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var startedAt = DateTimeOffset.UtcNow;
                try
                {
                    await _refresh.RefreshPublicTabsAsync(cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A transient Trade API or price-source failure must not
                    // stop automatic public-tab refreshes permanently.
                    MonitorStatusChanged?.Invoke(this, ClockMonitorStatus.Error);
                }

                var remaining = interval - (DateTimeOffset.UtcNow - startedAt);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected shutdown path.
        }
        catch
        {
            MonitorStatusChanged?.Invoke(this, ClockMonitorStatus.Error);
        }
    }
}
