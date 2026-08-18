namespace Poe2DesktopClock.Application.Models;

/// <summary>Оценка последнего успешного чтения публичных вкладок.</summary>
public sealed record PublicTabsValuation(
    decimal Divines,
    int UnpricedItems,
    bool IsComplete,
    DateTimeOffset UpdatedAt,
    string Summary)
{
    /// <summary>Details for every configured public tab from this refresh.</summary>
    public IReadOnlyList<PublicTabValuation> Tabs { get; init; } = [];
}
