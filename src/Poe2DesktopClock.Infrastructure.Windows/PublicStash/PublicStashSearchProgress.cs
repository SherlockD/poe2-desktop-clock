namespace Poe2DeskTracker.PublicStash;

internal sealed record PublicStashSearchProgress(
    int CompletedGroups,
    int TotalGroups,
    string Label,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    int ReturnedItemIds);
