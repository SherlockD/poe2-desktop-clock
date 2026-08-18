namespace Poe2DesktopClock.Application.Models;

/// <summary>Independent Currency-tab refresh result delivered to presentation.</summary>
public sealed record CurrencyRefreshResult(
    CurrencyValuation Valuation,
    DateTimeOffset? PricesUpdatedAt);
