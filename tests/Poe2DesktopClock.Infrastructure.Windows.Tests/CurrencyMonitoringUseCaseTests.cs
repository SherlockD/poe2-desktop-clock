using Poe2DesktopClock.Application.Interfaces;
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
            monitor,
            new TestGameStatusReader());
        useCase.MonitorStatusChanged += (_, status) => statuses.Add(status);

        await useCase.StartCurrencyMonitoringAsync();
        monitor.RaiseCurrencyChanged();
        await refresh.CurrencyRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        monitor.RaiseCurrencyChanged();
        Assert.Equal(1, refresh.CurrencyRefreshCalls);

        var stopTask = useCase.StopCurrencyMonitoringAsync();
        await refresh.CurrencyCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(stopTask.IsCompleted);
        refresh.CompleteCurrencyRefresh();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(1));

        monitor.RaiseCurrencyChanged();
        Assert.Equal(1, refresh.CurrencyRefreshCalls);

        await useCase.StartCurrencyMonitoringAsync();
        monitor.RaiseCurrencyChanged();
        Assert.Equal(2, refresh.CurrencyRefreshCalls);
        await useCase.StopCurrencyMonitoringAsync();

        Assert.Equal(ClockMonitorStatus.Stopped, statuses[^1]);
    }

    private sealed class TestSettingsUseCase : ITrackerSettingsUseCase
    {
        public TrackerSettings GetSettings() => TrackerSettings.Default;

        public void SaveSettings(TrackerSettings settings)
        {
        }
    }

    private sealed class ControlledRefreshUseCase : ITrackerRefreshUseCase
    {
        private static readonly ClockSnapshot EmptySnapshot = new(
            0m,
            0m,
            0m,
            null,
            null,
            null,
            false,
            string.Empty);
        private readonly TaskCompletionSource<ClockSnapshot> _currencyRefreshCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ClockSnapshot>? ClockSnapshotChanged;

        public TaskCompletionSource<bool> CurrencyRefreshStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CurrencyCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CurrencyRefreshCalls { get; private set; }

        public Task<ClockSnapshot> RefreshAsync(
            bool refreshPublicTabs,
            IProgress<TrackerProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (refreshPublicTabs)
            {
                return Task.FromResult(EmptySnapshot);
            }

            CurrencyRefreshCalls++;
            cancellationToken.Register(() => CurrencyCancellationObserved.TrySetResult(true));
            CurrencyRefreshStarted.TrySetResult(true);
            return _currencyRefreshCompletion.Task;
        }

        public void CompleteCurrencyRefresh()
        {
            _currencyRefreshCompletion.TrySetResult(EmptySnapshot);
            ClockSnapshotChanged?.Invoke(this, EmptySnapshot);
        }
    }

    private sealed class TestCurrencyChangeMonitor : ICurrencyChangeMonitor
    {
        public event EventHandler? CurrencyChanged;

        public event EventHandler<ClockMonitorStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task RunAsync(TimeSpan pollingPeriod, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public void RaiseCurrencyChanged() => CurrencyChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TestGameStatusReader : IGameStatusReader
    {
        public GameStatus GetGameStatus() => new(false, string.Empty);
    }
}
