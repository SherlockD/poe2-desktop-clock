namespace Poe2DeskTracker.PublicStash;

/// <summary>Light-weight result of a marker search, before item details are fetched.</summary>
public sealed record PublicStashSearchResult(
    string Label,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    string QueryId,
    IReadOnlyList<string> ItemIds)
{
    public bool IsTruncated => TotalMatches > ItemIds.Count;

    public PublicStashSearchGroupResult ToSearchGroupResult() =>
        new(Label, PriceAmount, PriceCurrency, TotalMatches, ItemIds.Count);
}
