namespace Poe2DesktopClock.Core.Models;

/// <summary>
/// Снимок значения, которое показывают виртуальные и будущие физические часы.
/// Public-вкладки обновляются реже, поэтому их время вынесено отдельно.
/// </summary>
public sealed record ClockSnapshot(
    decimal TotalDivines,
    decimal CurrencyTabDivines,
    decimal PublicTabsDivines,
    DateTimeOffset? CurrencyUpdatedAt,
    DateTimeOffset? PublicTabsUpdatedAt,
    DateTimeOffset? PricesUpdatedAt,
    bool IsComplete,
    string RussianSummary);
