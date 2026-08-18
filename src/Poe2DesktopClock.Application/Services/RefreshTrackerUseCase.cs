using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>
/// Application-сценарий обновления оценки. Он не знает о Win32, OCR, HTTP или JSON.
/// </summary>
public sealed class RefreshTrackerUseCase : ITrackerRefreshUseCase, IAsyncDisposable
{
    private readonly ITrackerSettingsUseCase _settings;
    private readonly IPriceSnapshotProvider _prices;
    private readonly ICurrencyValuationReader _currency;
    private readonly IPublicTabsValuationReader _publicTabs;
    private readonly IClockSnapshotComposer _snapshotComposer;
    private readonly ITrackerSnapshotPublisher _publisher;
    private readonly ILastClockSnapshotStore? _lastSnapshots;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CurrencyValuation? _latestCurrency;
    private PublicTabsValuation? _latestPublicTabs;
    private ClockSnapshot? _lastSnapshot;
    private int _disposeStarted;

    public RefreshTrackerUseCase(
        ITrackerSettingsUseCase settings,
        IPriceSnapshotProvider prices,
        ICurrencyValuationReader currency,
        IPublicTabsValuationReader publicTabs,
        IClockSnapshotComposer snapshotComposer,
        ITrackerSnapshotPublisher publisher,
        ILastClockSnapshotStore? lastSnapshots = null)
    {
        _settings = settings;
        _prices = prices;
        _currency = currency;
        _publicTabs = publicTabs;
        _snapshotComposer = snapshotComposer;
        _publisher = publisher;
        _lastSnapshots = lastSnapshots;
        _lastSnapshot = lastSnapshots?.GetLastSnapshot();
    }

    public event EventHandler<ClockSnapshot>? ClockSnapshotChanged
    {
        add => _publisher.ClockSnapshotChanged += value;
        remove => _publisher.ClockSnapshotChanged -= value;
    }

    public async Task<ClockSnapshot> RefreshCurrencyAsync(
        CurrencyTabFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = _settings.GetSettings();
            var prices = await GetPricesAsync(settings, progress: null, cancellationToken);
            var currency = await _currency.ReadAsync(frame, prices, cancellationToken);
            if (currency is not null)
            {
                _latestCurrency = currency;
            }

            return PublishSnapshot(prices?.RetrievedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ClockSnapshot> RefreshPublicTabsAsync(
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = _settings.GetSettings();
            var prices = await GetPricesAsync(settings, progress, cancellationToken);
            _latestPublicTabs = await _publicTabs.ReadAsync(settings, prices, progress, cancellationToken);
            return PublishSnapshot(prices?.RetrievedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private ClockSnapshot PublishSnapshot(DateTimeOffset? pricesUpdatedAt)
    {
        var snapshot = _snapshotComposer.Compose(
            _latestCurrency,
            _latestPublicTabs,
            pricesUpdatedAt,
            _lastSnapshot);
        if (HasAnyValuation(snapshot))
        {
            _lastSnapshots?.Save(snapshot);
            _lastSnapshot = snapshot;
        }

        _publisher.Publish(snapshot);
        return snapshot;
    }

    private static bool HasAnyValuation(ClockSnapshot snapshot) =>
        snapshot.CurrencyUpdatedAt is not null || snapshot.PublicTabsUpdatedAt is not null;

    private async Task<PriceSnapshot?> GetPricesAsync(
        TrackerSettings settings,
        IProgress<TrackerProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.League))
        {
            return null;
        }

        progress?.Report(new TrackerProgress("Обновляю цены в Divine..."));
        return await _prices.GetAsync(
            settings.League,
            TimeSpan.FromMinutes(settings.PriceRefreshIntervalMinutes),
            cancellationToken);
    }
}
