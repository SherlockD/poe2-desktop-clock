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
        DateTimeOffset? pricesUpdatedAt)
    {
        var total = (currency?.Divines ?? 0m) + (publicTabs?.Divines ?? 0m);
        var isComplete = currency is { UnpricedItems: 0, UnreadableSlots: 0 } && publicTabs is { IsComplete: true };
        var publicSummary = publicTabs?.Summary ?? "Публичные вкладки ещё не были обновлены.";
        var summary = isComplete
            ? $"Итого {total:0.##} Divine. Currency-вкладка и публичные вкладки актуальны."
            : $"Итого {total:0.##} Divine — частичная оценка. {publicSummary}";

        return new ClockSnapshot(
            total,
            currency?.Divines ?? 0m,
            publicTabs?.Divines ?? 0m,
            currency?.UpdatedAt,
            publicTabs?.UpdatedAt,
            pricesUpdatedAt,
            isComplete,
            summary);
    }
}
