using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;

namespace Poe2DeskTracker.Pricing;

/// <summary>Адаптирует poe.ninja к независимому application-порту цен.</summary>
public sealed class PoeNinjaPriceSnapshotProvider : IPriceSnapshotProvider
{
    private readonly PoeNinjaPriceClient _client;

    public PoeNinjaPriceSnapshotProvider(PoeNinjaPriceClient client) => _client = client;

    public async Task<PriceSnapshot?> GetAsync(
        string league,
        TimeSpan maximumAge,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _client.GetPricesAsync(league, maximumAge, cancellationToken);
        return new PriceSnapshot(snapshot.RetrievedAt, snapshot.PricesByNormalizedItemName);
    }
}
