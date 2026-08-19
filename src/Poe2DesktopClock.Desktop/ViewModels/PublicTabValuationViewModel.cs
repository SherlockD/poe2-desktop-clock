using System.Globalization;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Desktop.Localization;

namespace Poe2DesktopClock.Desktop.ViewModels;

/// <summary>Presentation-ready details for one public stash tab.</summary>
public sealed class PublicTabValuationViewModel
{
    public PublicTabValuationViewModel(PublicTabValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        Label = valuation.Label;
        TabName = valuation.TabName;
        Divines = valuation.Divines.ToString("N2", CultureInfo.InvariantCulture);
        ItemStacks = valuation.ItemStacks;
        TotalMatches = valuation.TotalMatches;
        ReturnedItemIds = valuation.ReturnedItemIds;
        HasTradeApiLimitWarning = valuation.IsTradeApiResultTruncated;
        HasUnpricedItemWarning = valuation.UnpricedItemTypes > 0;
        ItemSummary = AppStrings.Format("PublicTab_ItemStacksFormat", ItemStacks);
        PriceSummary = valuation.UnpricedItemTypes == 0
            ? AppStrings.Get("PublicTab_AllItemsPriced")
            : AppStrings.Format("PublicTab_UnpricedTypesFormat", valuation.UnpricedItemTypes);
        StatusSummary = CreateStatusSummary(valuation);
    }

    public string Label { get; }

    public string TabName { get; }

    public string Divines { get; }

    public int ItemStacks { get; }

    public int TotalMatches { get; }

    public int ReturnedItemIds { get; }

    public bool HasTradeApiLimitWarning { get; }

    public bool HasUnpricedItemWarning { get; }

    public string ItemSummary { get; }

    public string PriceSummary { get; }

    public string StatusSummary { get; }

    private static string CreateStatusSummary(PublicTabValuation valuation)
    {
        if (valuation.IsTradeApiResultTruncated)
        {
            return AppStrings.Format(
                "PublicTab_TruncatedFormat",
                valuation.TotalMatches,
                valuation.ReturnedItemIds);
        }

        if (!valuation.IsComplete)
        {
            return AppStrings.Get("PublicTab_Incomplete");
        }

        return AppStrings.Get("PublicTab_Complete");
    }
}
