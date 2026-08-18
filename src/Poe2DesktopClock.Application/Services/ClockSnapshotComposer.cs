using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>Собирает отображаемый снимок из независимых результатов сканирования.</summary>
public sealed class ClockSnapshotComposer : IClockSnapshotComposer
{
    public ClockSnapshot Compose(
        CurrencyValuation? currency,
        PublicTabsValuation? publicTabs,
        DateTimeOffset? pricesUpdatedAt,
        ClockSnapshot? previousSnapshot = null)
    {
        var currencyDivines = currency?.Divines ?? previousSnapshot?.CurrencyTabDivines ?? 0m;
        var publicTabsDivines = publicTabs?.Divines ?? previousSnapshot?.PublicTabsDivines ?? 0m;
        var currencyUpdatedAt = currency?.UpdatedAt ?? previousSnapshot?.CurrencyUpdatedAt;
        var publicTabsUpdatedAt = publicTabs?.UpdatedAt ?? previousSnapshot?.PublicTabsUpdatedAt;
        var actualPricesUpdatedAt = pricesUpdatedAt ?? previousSnapshot?.PricesUpdatedAt;
        var isCurrencyComplete = currency is { UnpricedItems: 0, UnreadableSlots: 0 } ||
                                 (currency is null && previousSnapshot is { IsComplete: true, CurrencyUpdatedAt: not null });
        var isPublicTabsComplete = publicTabs is { IsComplete: true, UnpricedItems: 0 } ||
                                   (publicTabs is null && previousSnapshot is { IsComplete: true, PublicTabsUpdatedAt: not null });
        var isComplete = isCurrencyComplete && isPublicTabsComplete;
        var total = currencyDivines + publicTabsDivines;
        var publicSummary = publicTabs?.Summary ?? "Публичные вкладки ещё не были обновлены.";
        var summary = isComplete
            ? $"Итого {total:0.##} Divine. Currency-вкладка и публичные вкладки актуальны."
            : $"Итого {total:0.##} Divine — частичная оценка. {publicSummary}";

        return new ClockSnapshot(
            total,
            currencyDivines,
            publicTabsDivines,
            currencyUpdatedAt,
            publicTabsUpdatedAt,
            actualPricesUpdatedAt,
            isComplete,
            summary);
    }
}
