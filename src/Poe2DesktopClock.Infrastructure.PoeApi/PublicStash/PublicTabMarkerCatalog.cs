using Poe2DesktopClock.Domain.Tracking;

namespace Poe2DeskTracker.PublicStash;

/// <summary>
/// Описывает восемь публичных вкладок, которые участвуют в оценке часов.
/// Уникальная цена — техническая метка Trade API, а не цена предметов.
/// </summary>
public static class PublicTabMarkerCatalog
{
    public static IReadOnlyList<PublicStashTabMarker> CreateDefaultMarkers() =>
        PublicTabDefaults.Items
            .Select(tab => new PublicStashTabMarker(tab.Label, tab.RequiredTabName, tab.MarkerPrice, tab.MarkerCurrency))
            .ToArray();
}
