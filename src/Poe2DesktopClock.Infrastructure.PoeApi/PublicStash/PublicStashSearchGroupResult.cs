namespace Poe2DeskTracker.PublicStash;

public sealed record PublicStashSearchGroupResult(
    string Label,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    int ReturnedItemIds)
{
    public bool IsTruncated => TotalMatches > ReturnedItemIds;
}
