namespace Poe2DeskTracker.PublicStash;

/// <summary>
/// User-selected public premium tabs to read through the PoE 2 trade API.
/// Each tab has a unique deliberately-high listing price. Trade cannot search
/// by stash name, so that marker price acts as a queryable tab identifier.
/// </summary>
public sealed record PublicStashSettings(
    string AccountName,
    string League,
    List<string> TabNames,
    List<PublicStashTabMarker>? TabMarkers = null)
{
    public bool HasCompleteMarkers => TabMarkers is { Count: > 0 } &&
        TabMarkers.Count == TabNames.Count;
}

public sealed record PublicStashTabMarker(
    string Label,
    string TabName,
    decimal PriceAmount,
    string PriceCurrency);
