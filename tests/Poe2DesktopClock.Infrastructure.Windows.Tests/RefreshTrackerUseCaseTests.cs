using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class RefreshTrackerUseCaseTests
{
    [Fact]
    public async Task Currency_refresh_uses_the_supplied_monitor_frame_only()
    {
        var currency = new TestCurrencyValuationReader();
        var publicTabs = new TestPublicTabsValuationReader();
        await using var useCase = CreateUseCase(currency, publicTabs);
        var frame = new CurrencyTabFrame(new byte[] { 1, 2, 3 }, DateTimeOffset.UtcNow);

        await useCase.RefreshCurrencyAsync(frame);

        Assert.Same(frame, currency.LastFrame);
        Assert.Equal(1, currency.ReadCalls);
        Assert.Equal(0, publicTabs.ReadCalls);
    }

    [Fact]
    public async Task Public_tabs_refresh_does_not_read_currency()
    {
        var currency = new TestCurrencyValuationReader();
        var publicTabs = new TestPublicTabsValuationReader();
        await using var useCase = CreateUseCase(currency, publicTabs);

        await useCase.RefreshPublicTabsAsync();

        Assert.Equal(0, currency.ReadCalls);
        Assert.Equal(1, publicTabs.ReadCalls);
    }

    [Fact]
    public async Task Currency_refresh_keeps_the_last_persisted_public_value_and_saves_the_merged_snapshot()
    {
        var previousUpdatedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var persisted = new ClockSnapshot(
            TotalDivines: 100m,
            CurrencyTabDivines: 60m,
            PublicTabsDivines: 40m,
            CurrencyUpdatedAt: previousUpdatedAt,
            PublicTabsUpdatedAt: previousUpdatedAt,
            PricesUpdatedAt: previousUpdatedAt,
            IsComplete: true,
            RussianSummary: "Итого 100 Divine.");
        var store = new TestLastClockSnapshotStore(persisted);
        var updatedAt = previousUpdatedAt.AddMinutes(5);
        var currency = new TestCurrencyValuationReader(new CurrencyValuation(75m, 0, 0, updatedAt));
        var publicTabs = new TestPublicTabsValuationReader();
        await using var useCase = CreateUseCase(currency, publicTabs, store);

        var snapshot = await useCase.RefreshCurrencyAsync(new CurrencyTabFrame(new byte[] { 1 }, updatedAt));

        Assert.Equal(115m, snapshot.TotalDivines);
        Assert.Equal(75m, snapshot.CurrencyTabDivines);
        Assert.Equal(40m, snapshot.PublicTabsDivines);
        Assert.Equal(previousUpdatedAt, snapshot.PublicTabsUpdatedAt);
        Assert.Equal(snapshot, store.LastSnapshot);
        Assert.Equal(1, store.SaveCalls);
    }

    private static RefreshTrackerUseCase CreateUseCase(
        ICurrencyValuationReader currency,
        IPublicTabsValuationReader publicTabs,
        ILastClockSnapshotStore? lastSnapshots = null) =>
        new(
            new TestSettingsUseCase(),
            new TestPriceSnapshotProvider(),
            currency,
            publicTabs,
            new ClockSnapshotComposer(),
            new TrackerSnapshotPublisher(),
            lastSnapshots);

    private sealed class TestSettingsUseCase : ITrackerSettingsUseCase
    {
        public TrackerSettings GetSettings() => TrackerSettings.Default;

        public void SaveSettings(TrackerSettings settings)
        {
        }
    }

    private sealed class TestPriceSnapshotProvider : IPriceSnapshotProvider
    {
        public Task<PriceSnapshot?> GetAsync(
            string league,
            TimeSpan maximumAge,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PriceSnapshot?>(null);
    }

    private sealed class TestCurrencyValuationReader : ICurrencyValuationReader
    {
        private readonly CurrencyValuation? _result;

        public TestCurrencyValuationReader(CurrencyValuation? result = null) => _result = result;

        public int ReadCalls { get; private set; }

        public CurrencyTabFrame? LastFrame { get; private set; }

        public Task<CurrencyValuation?> ReadAsync(
            CurrencyTabFrame frame,
            PriceSnapshot? prices,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            LastFrame = frame;
            return Task.FromResult<CurrencyValuation?>(
                _result ?? new CurrencyValuation(0m, 0, 0, frame.CapturedAt));
        }
    }

    private sealed class TestPublicTabsValuationReader : IPublicTabsValuationReader
    {
        public int ReadCalls { get; private set; }

        public Task<PublicTabsValuation> ReadAsync(
            TrackerSettings settings,
            PriceSnapshot? prices,
            IProgress<TrackerProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(new PublicTabsValuation(0m, 0, true, DateTimeOffset.UtcNow, string.Empty));
        }
    }

    private sealed class TestLastClockSnapshotStore(ClockSnapshot? snapshot) : ILastClockSnapshotStore
    {
        public ClockSnapshot? LastSnapshot { get; private set; } = snapshot;

        public int SaveCalls { get; private set; }

        public ClockSnapshot? GetLastSnapshot() => LastSnapshot;

        public void Save(ClockSnapshot snapshot)
        {
            LastSnapshot = snapshot;
            SaveCalls++;
        }
    }
}
