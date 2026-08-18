using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class RefreshTrackerUseCaseTests
{
    [Fact]
    public async Task Currency_refresh_uses_only_the_supplied_monitor_frame_and_publishes_its_result()
    {
        var currency = new TestCurrencyValuationReader();
        using var useCase = CreateCurrencyUseCase(currency);
        var frame = new CurrencyTabFrame(new byte[] { 1, 2, 3 }, DateTimeOffset.UtcNow);
        CurrencyRefreshResult? published = null;
        useCase.Refreshed += (_, result) => published = result;

        var result = await useCase.RefreshAsync(frame);

        Assert.Same(frame, currency.LastFrame);
        Assert.Equal(1, currency.ReadCalls);
        Assert.Same(result, published);
    }

    [Fact]
    public async Task Public_tabs_refresh_publishes_an_independent_result()
    {
        var publicTabs = new TestPublicTabsValuationReader();
        using var useCase = CreatePublicTabsUseCase(publicTabs);
        PublicTabsRefreshResult? published = null;
        useCase.Refreshed += (_, result) => published = result;

        var result = await useCase.RefreshAsync();

        Assert.Equal(1, publicTabs.ReadCalls);
        Assert.Same(result, published);
    }

    [Fact]
    public async Task Currency_refresh_is_not_blocked_by_a_long_public_tabs_refresh()
    {
        var updatedAt = new DateTimeOffset(2026, 8, 18, 15, 0, 0, TimeSpan.Zero);
        var currencyReader = new TestCurrencyValuationReader(new CurrencyValuation(75m, 0, 0, updatedAt));
        var publicTabsReader = new BlockingPublicTabsValuationReader();
        using var currency = CreateCurrencyUseCase(currencyReader);
        using var publicTabs = CreatePublicTabsUseCase(publicTabsReader);

        var publicRefresh = publicTabs.RefreshAsync();
        await publicTabsReader.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var currencyResult = await currency
            .RefreshAsync(new CurrencyTabFrame(new byte[] { 1 }, updatedAt))
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(75m, currencyResult?.Valuation.Divines);
        Assert.False(publicRefresh.IsCompleted);

        publicTabsReader.Complete(new PublicTabsValuation(25m, 0, true, updatedAt, string.Empty));
        await publicRefresh.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static CurrencyRefreshUseCase CreateCurrencyUseCase(ICurrencyValuationReader currency) =>
        new(new TestSettingsUseCase(), new TestPriceSnapshotProvider(), currency);

    private static PublicTabsRefreshUseCase CreatePublicTabsUseCase(IPublicTabsValuationReader publicTabs) =>
        new(new TestSettingsUseCase(), new TestPriceSnapshotProvider(), publicTabs);

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

    private sealed class BlockingPublicTabsValuationReader : IPublicTabsValuationReader
    {
        private readonly TaskCompletionSource<PublicTabsValuation> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PublicTabsValuation> ReadAsync(
            TrackerSettings settings,
            PriceSnapshot? prices,
            IProgress<TrackerProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
            return _completion.Task;
        }

        public void Complete(PublicTabsValuation valuation) => _completion.TrySetResult(valuation);
    }
}
