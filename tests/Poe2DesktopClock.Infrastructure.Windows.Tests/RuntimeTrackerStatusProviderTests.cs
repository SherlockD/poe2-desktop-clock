using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.Services;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class RuntimeTrackerStatusProviderTests
{
    [Fact]
    public async Task Frontend_combines_the_latest_independent_source_notifications()
    {
        var currency = new TestCurrencyRefreshUseCase();
        var publicTabs = new TestPublicTabsRefreshUseCase();
        var store = new TestLastClockSnapshotStore();
        var publisher = new TrackerSnapshotPublisher();
        var device = new StubDeviceSynchronizationUseCase();
        await using var relay = new DeviceSnapshotRelay(publisher, device);
        await using var provider = new RuntimeTrackerStatusProvider(
            currency,
            publicTabs,
            new TestMonitoringUseCase(),
            new GameSessionUseCase(store),
            device,
            store,
            relay,
            new ClockSnapshotComposer(),
            publisher);
        var updatedAt = new DateTimeOffset(2026, 8, 18, 15, 0, 0, TimeSpan.Zero);

        currency.Raise(new CurrencyRefreshResult(
            new CurrencyValuation(75m, 0, 0, updatedAt),
            updatedAt));
        Assert.Equal(75m, provider.GetCurrent().ClockSnapshot?.TotalDivines);

        publicTabs.Raise(new PublicTabsRefreshResult(
            new PublicTabsValuation(25m, 0, true, updatedAt, string.Empty)
            {
                Tabs =
                [
                    new PublicTabValuation("Разлом", "~price 1001 mirror", 25m, 5, 5, 5, 0, true),
                ],
            },
            updatedAt));

        var combined = provider.GetCurrent().ClockSnapshot;
        Assert.Equal(100m, combined?.TotalDivines);
        Assert.Equal(75m, combined?.CurrencyTabDivines);
        Assert.Equal(25m, combined?.PublicTabsDivines);
        Assert.Equal(combined, store.LastSnapshot);
        Assert.Single(provider.GetCurrent().PublicTabsValuation!.Tabs);
    }

    [Fact]
    public async Task Repeated_monitor_status_does_not_enqueue_redundant_frontend_updates()
    {
        var currency = new TestCurrencyRefreshUseCase();
        var publicTabs = new TestPublicTabsRefreshUseCase();
        var monitoring = new TestMonitoringUseCase();
        var store = new TestLastClockSnapshotStore();
        var publisher = new TrackerSnapshotPublisher();
        var device = new StubDeviceSynchronizationUseCase();
        await using var relay = new DeviceSnapshotRelay(publisher, device);
        await using var provider = new RuntimeTrackerStatusProvider(
            currency,
            publicTabs,
            monitoring,
            new GameSessionUseCase(store),
            device,
            store,
            relay,
            new ClockSnapshotComposer(),
            publisher);
        var notifications = 0;
        provider.StatusChanged += (_, _) => notifications++;

        monitoring.RaiseStatus(ClockMonitorStatus.Tracking);
        monitoring.RaiseStatus(ClockMonitorStatus.Tracking);

        Assert.Equal(1, notifications);
    }

    private sealed class TestCurrencyRefreshUseCase : ICurrencyRefreshUseCase
    {
        public event EventHandler<CurrencyRefreshResult>? Refreshed;

        public Task<CurrencyRefreshResult?> RefreshAsync(
            CurrencyTabFrame frame,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrencyRefreshResult?>(null);

        public void Raise(CurrencyRefreshResult result) => Refreshed?.Invoke(this, result);
    }

    private sealed class TestPublicTabsRefreshUseCase : IPublicTabsRefreshUseCase
    {
        public event EventHandler<PublicTabsRefreshResult>? Refreshed;

        public Task<PublicTabsRefreshResult> RefreshAsync(
            IProgress<TrackerProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Raise(PublicTabsRefreshResult result) => Refreshed?.Invoke(this, result);
    }

    private sealed class TestMonitoringUseCase : ITrackerMonitoringUseCase
    {
        public event EventHandler<ClockMonitorStatus>? MonitorStatusChanged;

        public GameStatus GetGameStatus() => new(false, string.Empty);

        public Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopCurrencyMonitoringAsync() => Task.CompletedTask;

        public void RaiseStatus(ClockMonitorStatus status) =>
            MonitorStatusChanged?.Invoke(this, status);
    }

    private sealed class TestLastClockSnapshotStore : ILastClockSnapshotStore
    {
        public ClockSnapshot? LastSnapshot { get; private set; }

        public ClockSnapshot? GetLastSnapshot() => LastSnapshot;

        public void Save(ClockSnapshot snapshot) => LastSnapshot = snapshot;
    }
}
