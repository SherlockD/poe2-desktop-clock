using System.Globalization;
using Poe2DesktopClock.Application.Models;

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
        ItemSummary = $"Учтено стаков: {ItemStacks}";
        PriceSummary = valuation.UnpricedItemTypes == 0
            ? "Все типы предметов получили цену"
            : $"Типов без цены: {valuation.UnpricedItemTypes}";
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
            return $"Trade API нашёл {valuation.TotalMatches} предметов, но вернул {valuation.ReturnedItemIds}. " +
                   "Лимит выдачи достигнут: оценка этой вкладки неполная.";
        }

        if (!valuation.IsComplete)
        {
            return "Вкладка прочитана не полностью: проверьте имя вкладки и цену-маркер.";
        }

        return "Trade API вернул все найденные предметы.";
    }
}
