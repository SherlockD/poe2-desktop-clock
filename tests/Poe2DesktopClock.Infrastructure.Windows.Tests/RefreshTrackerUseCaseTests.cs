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

    private static RefreshTrackerUseCase CreateUseCase(
        ICurrencyValuationReader currency,
        IPublicTabsValuationReader publicTabs) =>
        new(
            new TestSettingsUseCase(),
            new TestPriceSnapshotProvider(),
            currency,
            publicTabs,
            new ClockSnapshotComposer(),
            new TrackerSnapshotPublisher());

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
        public int ReadCalls { get; private set; }

        public CurrencyTabFrame? LastFrame { get; private set; }

        public Task<CurrencyValuation?> ReadAsync(
            CurrencyTabFrame frame,
            PriceSnapshot? prices,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            LastFrame = frame;
            return Task.FromResult<CurrencyValuation?>(new CurrencyValuation(0m, 0, 0, frame.CapturedAt));
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
}
