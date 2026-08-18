using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class CurrencyMonitoringUseCaseTests
{
    [Fact]
    public async Task Stop_waits_for_active_currency_refresh_and_monitoring_can_restart()
    {
        var monitor = new TestCurrencyChangeMonitor();
        var refresh = new ControlledRefreshUseCase();
        var statuses = new List<ClockMonitorStatus>();
        await using var useCase = new CurrencyMonitoringUseCase(
            new TestSettingsUseCase(),
            refresh,
            new TestPublicTabsRefreshUseCase(),
            monitor,
            new TestGameStatusReader());
        useCase.MonitorStatusChanged += (_, status) => statuses.Add(status);

        await useCase.StartCurrencyMonitoringAsync();
        var firstFrame = CreateFrame(1);
        monitor.RaiseCurrencyChanged(firstFrame);
        await refresh.CurrencyRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(firstFrame, refresh.CurrencyFrames[0]);
        monitor.RaiseCurrencyChanged(CreateFrame(2));
        Assert.Equal(1, refresh.CurrencyRefreshCalls);

        var stopTask = useCase.StopCurrencyMonitoringAsync();
        await refresh.CurrencyCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(stopTask.IsCompleted);
        refresh.CompleteCurrencyRefresh();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(1));

        monitor.RaiseCurrencyChanged(CreateFrame(3));
        Assert.Equal(1, refresh.CurrencyRefreshCalls);

        await useCase.StartCurrencyMonitoringAsync();
        var restartedFrame = CreateFrame(4);
        monitor.RaiseCurrencyChanged(restartedFrame);
        await refresh.SecondCurrencyRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, refresh.CurrencyRefreshCalls);
        Assert.Same(restartedFrame, refresh.CurrencyFrames[1]);
        await useCase.StopCurrencyMonitoringAsync();

        Assert.Equal(ClockMonitorStatus.Stopped, statuses[^1]);
    }

    [Fact]
    public async Task Change_during_refresh_processes_only_the_latest_pending_frame()
    {
        var monitor = new TestCurrencyChangeMonitor();
        var refresh = new ControlledRefreshUseCase();
        await using var useCase = new CurrencyMonitoringUseCase(
            new TestSettingsUseCase(),
            refresh,
            new TestPublicTabsRefreshUseCase(),
            monitor,
            new TestGameStatusReader());

        await useCase.StartCurrencyMonitoringAsync();
        var firstFrame = CreateFrame(1);
        var supersededFrame = CreateFrame(2);
        var latestFrame = CreateFrame(3);
        monitor.RaiseCurrencyChanged(firstFrame);
        await refresh.CurrencyRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        monitor.RaiseCurrencyChanged(supersededFrame);
        monitor.RaiseCurrencyChanged(latestFrame);

        Assert.Equal(1, refresh.CurrencyRefreshCalls);
        refresh.CompleteCurrencyRefresh();
        await refresh.SecondCurrencyRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, refresh.CurrencyRefreshCalls);
        Assert.Same(firstFrame, refresh.CurrencyFrames[0]);
        Assert.Same(latestFrame, refresh.CurrencyFrames[1]);
        await useCase.StopCurrencyMonitoringAsync();
    }

    [Fact]
    public async Task Dispose_is_idempotent_after_monitoring_has_stopped()
    {
        var useCase = new CurrencyMonitoringUseCase(
            new TestSettingsUseCase(),
            new ControlledRefreshUseCase(),
            new TestPublicTabsRefreshUseCase(),
            new TestCurrencyChangeMonitor(),
            new TestGameStatusReader());

        await useCase.DisposeAsync();
        await useCase.DisposeAsync();
        await useCase.StopCurrencyMonitoringAsync();
    }

    private static CurrencyTabFrame CreateFrame(byte marker) =>
        new(new byte[] { marker }, DateTimeOffset.UtcNow);

    private sealed class TestSettingsUseCase : ITrackerSettingsUseCase
    {
        public TrackerSettings GetSettings() => TrackerSettings.Default;

        public void SaveSettings(TrackerSettings settings)
        {
        }
    }

    private sealed class ControlledRefreshUseCase : ICurrencyRefreshUseCase
    {
        private static readonly CurrencyRefreshResult EmptyResult = new(
            new CurrencyValuation(0m, 0, 0, DateTimeOffset.UtcNow),
            PricesUpdatedAt: null);
        private readonly TaskCompletionSource<CurrencyRefreshResult?> _currencyRefreshCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<CurrencyRefreshResult>? Refreshed
        {
            add { }
            remove { }
        }

        public TaskCompletionSource<bool> CurrencyRefreshStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CurrencyCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SecondCurrencyRefreshStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CurrencyRefreshCalls { get; private set; }

        public List<CurrencyTabFrame> CurrencyFrames { get; } = [];

        public Task<CurrencyRefreshResult?> RefreshAsync(
            CurrencyTabFrame frame,
            CancellationToken cancellationToken = default)
        {
            CurrencyRefreshCalls++;
            CurrencyFrames.Add(frame);
            cancellationToken.Register(() => CurrencyCancellationObserved.TrySetResult(true));
            CurrencyRefreshStarted.TrySetResult(true);
            if (CurrencyRefreshCalls == 2)
            {
                SecondCurrencyRefreshStarted.TrySetResult(true);
            }

            return _currencyRefreshCompletion.Task;
        }

        public void CompleteCurrencyRefresh()
        {
            _currencyRefreshCompletion.TrySetResult(EmptyResult);
        }
    }

    private sealed class TestPublicTabsRefreshUseCase : IPublicTabsRefreshUseCase
    {
        public event EventHandler<PublicTabsRefreshResult>? Refreshed
        {
            add { }
            remove { }
        }

        public Task<PublicTabsRefreshResult> RefreshAsync(
            IProgress<TrackerProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublicTabsRefreshResult(
                new PublicTabsValuation(0m, 0, true, DateTimeOffset.UtcNow, string.Empty),
                PricesUpdatedAt: null));
    }

    private sealed class TestCurrencyChangeMonitor : ICurrencyChangeMonitor
    {
        public event EventHandler<CurrencyTabChangedEventArgs>? CurrencyChanged;

        public event EventHandler<ClockMonitorStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task RunAsync(TimeSpan pollingPeriod, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public void RaiseCurrencyChanged(CurrencyTabFrame frame) =>
            CurrencyChanged?.Invoke(this, new CurrencyTabChangedEventArgs(frame));
    }

    private sealed class TestGameStatusReader : IGameStatusReader
    {
        public GameStatus GetGameStatus() => new(false, string.Empty);
    }
}
