using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>Refreshes only the configured public-stash tabs.</summary>
public sealed class PublicTabsRefreshUseCase : IPublicTabsRefreshUseCase, IDisposable
{
    private readonly ITrackerSettingsUseCase _settings;
    private readonly IPriceSnapshotProvider _prices;
    private readonly IPublicTabsValuationReader _publicTabs;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PublicTabsRefreshUseCase(
        ITrackerSettingsUseCase settings,
        IPriceSnapshotProvider prices,
        IPublicTabsValuationReader publicTabs)
    {
        _settings = settings;
        _prices = prices;
        _publicTabs = publicTabs;
    }

    public event EventHandler<PublicTabsRefreshResult>? Refreshed;

    public async Task<PublicTabsRefreshResult> RefreshAsync(
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _settings.GetSettings();
            var prices = await GetPricesAsync(settings, progress, cancellationToken).ConfigureAwait(false);
            var valuation = await _publicTabs
                .ReadAsync(settings, prices, progress, cancellationToken)
                .ConfigureAwait(false);
            var result = new PublicTabsRefreshResult(valuation, prices?.RetrievedAt);
            Refreshed?.Invoke(this, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

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
            cancellationToken).ConfigureAwait(false);
    }
}
