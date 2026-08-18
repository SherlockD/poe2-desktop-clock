namespace Poe2DesktopClock.Application.Models;

/// <summary>Valuation and Trade API coverage for one configured public tab.</summary>
public sealed record PublicTabValuation(
    string Label,
    string TabName,
    decimal Divines,
    int TotalMatches,
    int ReturnedItemIds,
    int ItemStacks,
    int UnpricedItemTypes,
    bool IsComplete)
{
    /// <summary>
    /// The Trade API found more listings than it returned IDs for. The
    /// displayed value is therefore only a lower-bound estimate.
    /// </summary>
    public bool IsTradeApiResultTruncated => TotalMatches > ReturnedItemIds;
}
