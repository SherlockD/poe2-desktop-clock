namespace Poe2DesktopClock.Application.Models;

/// <summary>Independent public-tabs refresh result delivered to presentation.</summary>
public sealed record PublicTabsRefreshResult(
    PublicTabsValuation Valuation,
    DateTimeOffset? PricesUpdatedAt);
