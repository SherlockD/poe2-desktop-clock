using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.PublicStash;
using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

/// <summary>
/// Applies the tracked-tab and completeness rules to one Trade API discovery result.
/// </summary>
internal static class PublicStashValuationCalculator
{
    internal static PublicTabsValuation Calculate(
        PublicStashDiscovery discovery,
        IReadOnlyList<PublicStashTabMarker> markers,
        PoeNinjaPriceSnapshot? prices,
        DateTimeOffset updatedAt)
    {
        var tabNames = markers.Select(marker => marker.TabName).ToHashSet(StringComparer.Ordinal);
        var selectedItems = discovery.Items
            .Where(item => tabNames.Contains(item.TabName))
            .GroupBy(item => item.Id ?? $"{item.TabName}\u001f{item.X}\u001f{item.Y}\u001f{item.ItemName}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var totalDivines = 0m;
        var unpricedItems = 0;
        foreach (var itemGroup in selectedItems.GroupBy(item => item.ItemName, StringComparer.Ordinal))
        {
            if (prices is null || !prices.TryGetDivinePrice(itemGroup.Key, out var unitDivines))
            {
                unpricedItems++;
                continue;
            }

            totalDivines += unitDivines * itemGroup.Sum(item => item.StackSize);
        }

        var isComplete = !discovery.IsTruncated;
        foreach (var marker in markers)
        {
            var markerItems = discovery.Items
                .Where(item => string.Equals(item.MarkerLabel, marker.Label, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (markerItems.Any(item => !string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal)) ||
                !selectedItems.Any(item => string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal)))
            {
                isComplete = false;
            }
        }

        var summary = isComplete
            ? $"Публичные вкладки: {selectedItems.Length} стаков, оценка обновлена."
            : "Публичные вкладки прочитаны частично: проверьте предупреждения и названия вкладок.";
        return new PublicTabsValuation(totalDivines, unpricedItems, isComplete, updatedAt, summary);
    }
}
