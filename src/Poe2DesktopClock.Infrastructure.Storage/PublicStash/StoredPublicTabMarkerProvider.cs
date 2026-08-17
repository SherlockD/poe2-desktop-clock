using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>JSON-backed implementation of the public-tab configuration port.</summary>
public sealed class StoredPublicTabMarkerProvider : IPublicTabMarkerProvider
{
    private readonly PublicStashSettingsStore _store;

    public StoredPublicTabMarkerProvider()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Poe2DeskTracker");
        _store = new PublicStashSettingsStore(Path.Combine(directory, "public-stash.json"));
    }

    public IReadOnlyList<PublicTabMarker> GetMarkers()
    {
        var stored = _store.Get();
        var markers = stored is { HasCompleteMarkers: true }
            ? stored.TabMarkers!
            : PublicTabMarkerCatalog.CreateDefaultMarkers();
        return markers.Select(marker => new PublicTabMarker(marker.Label, marker.TabName, marker.PriceAmount, marker.PriceCurrency)).ToArray();
    }
}
