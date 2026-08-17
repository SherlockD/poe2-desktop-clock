namespace Poe2DeskTracker.PublicStash;

public sealed record PublicStashSearchProgress(
    int CompletedGroups,
    int TotalGroups,
    string Label,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    int ReturnedItemIds);
