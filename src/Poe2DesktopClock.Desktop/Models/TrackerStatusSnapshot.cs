namespace Poe2DesktopClock.Desktop.Models;

public sealed record TrackerStatusSnapshot(
    decimal TotalDivines,
    DateTimeOffset? CurrencyUpdatedAt,
    DateTimeOffset? PublicStashUpdatedAt,
    DateTimeOffset? PricesUpdatedAt,
    string CurrencyStatus,
    string PublicStashStatus,
    bool IsEstimateComplete);
