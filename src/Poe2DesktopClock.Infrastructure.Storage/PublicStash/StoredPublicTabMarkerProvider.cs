using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>JSON-backed implementation of the public-tab configuration port.</summary>
public sealed class StoredPublicTabMarkerProvider : IPublicTabMarkerProvider
{
    private readonly PublicStashSettingsStore _store;

    public StoredPublicTabMarkerProvider()
        : this(CreateStore())
    {
    }

    public StoredPublicTabMarkerProvider(PublicStashSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public IReadOnlyList<PublicTabMarker> GetMarkers()
    {
        var stored = _store.Get();
        var markers = stored is { HasCompleteMarkers: true }
            ? stored.TabMarkers!
            : PublicTabMarkerCatalog.CreateDefaultMarkers();
        return markers.Select(marker => new PublicTabMarker(marker.Label, marker.TabName, marker.PriceAmount, marker.PriceCurrency)).ToArray();
    }

    private static PublicStashSettingsStore CreateStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Poe2DeskTracker");
        return new PublicStashSettingsStore(Path.Combine(directory, "public-stash.json"));
    }
}
