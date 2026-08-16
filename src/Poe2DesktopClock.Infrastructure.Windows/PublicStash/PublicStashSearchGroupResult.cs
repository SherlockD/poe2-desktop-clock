namespace Poe2DeskTracker.PublicStash;

internal sealed record PublicStashSearchGroupResult(
    string Label,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    int ReturnedItemIds)
{
    internal bool IsTruncated => TotalMatches > ReturnedItemIds;
}
