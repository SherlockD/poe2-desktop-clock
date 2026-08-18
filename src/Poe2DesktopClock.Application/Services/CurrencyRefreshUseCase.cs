using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>Refreshes only the Currency-tab valuation.</summary>
public sealed class CurrencyRefreshUseCase : ICurrencyRefreshUseCase, IDisposable
{
    private readonly ITrackerSettingsUseCase _settings;
    private readonly IPriceSnapshotProvider _prices;
    private readonly ICurrencyValuationReader _currency;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CurrencyRefreshUseCase(
        ITrackerSettingsUseCase settings,
        IPriceSnapshotProvider prices,
        ICurrencyValuationReader currency)
    {
        _settings = settings;
        _prices = prices;
        _currency = currency;
    }

    public event EventHandler<CurrencyRefreshResult>? Refreshed;

    public async Task<CurrencyRefreshResult?> RefreshAsync(
        CurrencyTabFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _settings.GetSettings();
            var prices = await GetPricesAsync(settings, cancellationToken).ConfigureAwait(false);
            var valuation = await _currency.ReadAsync(frame, prices, cancellationToken).ConfigureAwait(false);
            if (valuation is null)
            {
                return null;
            }

            var result = new CurrencyRefreshResult(valuation, prices?.RetrievedAt);
            Refreshed?.Invoke(this, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private Task<PriceSnapshot?> GetPricesAsync(
        TrackerSettings settings,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(settings.League)
            ? Task.FromResult<PriceSnapshot?>(null)
            : _prices.GetAsync(
                settings.League,
                TimeSpan.FromMinutes(settings.PriceRefreshIntervalMinutes),
                cancellationToken);
}
