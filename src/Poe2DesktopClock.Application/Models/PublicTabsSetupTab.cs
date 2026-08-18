namespace Poe2DesktopClock.Application.Models;

/// <summary>
/// A public-tab marker offered by the initial setup flow. The marker price is
/// a technical Trade API identifier and is not an item valuation.
/// </summary>
public sealed record PublicTabsSetupTab(
    string Label,
    string TabName,
    decimal PriceAmount,
    string PriceCurrency,
    bool IsSelected);
