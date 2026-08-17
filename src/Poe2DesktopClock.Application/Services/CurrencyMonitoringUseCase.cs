using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>Owns monitoring lifetime and requests an application refresh on a detected change.</summary>
public sealed class CurrencyMonitoringUseCase : ITrackerMonitoringUseCase, IAsyncDisposable
{
    private readonly ITrackerSettingsUseCase _settings;
    private readonly ITrackerRefreshUseCase _refresh;
    private readonly ICurrencyChangeMonitor _monitor;
    private readonly IGameStatusReader _gameStatus;
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private Task? _automaticRefreshTask;
    private int _refreshInProgress;

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

    public Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_runTask is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var settings = _settings.GetSettings();
        var period = TimeSpan.FromSeconds(1d / settings.CurrencyScreensPerSecond);
        _runTask = _monitor.RunAsync(period, _cancellation.Token);
        _automaticRefreshTask = settings.IsAutomaticPublicRefreshEnabled
            ? RefreshPublicTabsPeriodicallyAsync(TimeSpan.FromMinutes(settings.PublicRefreshIntervalMinutes), _cancellation.Token)
            : null;
        return Task.CompletedTask;
    }

    public async Task StopCurrencyMonitoringAsync()
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (_runTask is not null)
            {
                await _runTask;
            }
            if (_automaticRefreshTask is not null)
            {
                await _automaticRefreshTask;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected shutdown path.
        }
        finally
        {
            cancellation.Dispose();
            _runTask = null;
            _automaticRefreshTask = null;
            MonitorStatusChanged?.Invoke(this, ClockMonitorStatus.Stopped);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopCurrencyMonitoringAsync();
        _monitor.CurrencyChanged -= OnCurrencyChanged;
        _monitor.StatusChanged -= OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, ClockMonitorStatus status) =>
        MonitorStatusChanged?.Invoke(this, status);

    private async void OnCurrencyChanged(object? sender, EventArgs eventArgs)
    {
        if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await _refresh.RefreshAsync(refreshPublicTabs: false);
        }
        catch
        {
            MonitorStatusChanged?.Invoke(this, ClockMonitorStatus.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    private async Task RefreshPublicTabsPeriodicallyAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await _refresh.RefreshAsync(refreshPublicTabs: true, cancellationToken: cancellationToken);
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await _refresh.RefreshAsync(refreshPublicTabs: true, cancellationToken: cancellationToken);
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
