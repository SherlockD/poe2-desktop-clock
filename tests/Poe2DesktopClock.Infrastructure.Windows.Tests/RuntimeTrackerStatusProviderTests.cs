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
            new PublicTabsValuation(25m, 0, true, updatedAt, string.Empty),
            updatedAt));

        var combined = provider.GetCurrent().ClockSnapshot;
        Assert.Equal(100m, combined?.TotalDivines);
        Assert.Equal(75m, combined?.CurrencyTabDivines);
        Assert.Equal(25m, combined?.PublicTabsDivines);
        Assert.Equal(combined, store.LastSnapshot);
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
        public event EventHandler<ClockMonitorStatus>? MonitorStatusChanged
        {
            add { }
            remove { }
        }

        public GameStatus GetGameStatus() => new(false, string.Empty);

        public Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopCurrencyMonitoringAsync() => Task.CompletedTask;
    }

    private sealed class TestLastClockSnapshotStore : ILastClockSnapshotStore
    {
        public ClockSnapshot? LastSnapshot { get; private set; }

        public ClockSnapshot? GetLastSnapshot() => LastSnapshot;

        public void Save(ClockSnapshot snapshot) => LastSnapshot = snapshot;
    }
}
