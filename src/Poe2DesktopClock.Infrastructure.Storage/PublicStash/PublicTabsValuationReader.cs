using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Domain.Tracking;
using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>Reads persisted tab markers and uses Trade API to value them.</summary>
public sealed class PublicTabsValuationReader : IPublicTabsValuationReader
{
    private readonly IPublicTabMarkerProvider _markers;
    private readonly TradeApiClient _tradeApi;

    public PublicTabsValuationReader(TradeApiClient tradeApi, IPublicTabMarkerProvider markers)
    {
        _tradeApi = tradeApi;
        _markers = markers;
    }

    public async Task<PublicTabsValuation> ReadAsync(
        TrackerSettings settings,
        PriceSnapshot? prices,
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountName) || string.IsNullOrWhiteSpace(settings.League))
        {
            return new PublicTabsValuation(0m, 0, false, DateTimeOffset.UtcNow, "Не заполнены имя аккаунта или лига для публичных вкладок.");
        }

        var markers = _markers.GetMarkers()
            .Select(marker => new PublicStashTabMarker(marker.Label, marker.TabName, marker.PriceAmount, marker.PriceCurrency))
            .ToArray();
        var discovery = await _tradeApi.DiscoverPublicTabItemsAsync(
            settings.AccountName,
            settings.League,
            markers,
            new Progress<PublicStashSearchProgress>(item => progress?.Report(
                new TrackerProgress($"Публичные вкладки: {item.CompletedGroups}/{item.TotalGroups} — {item.Label}.", item.CompletedGroups, item.TotalGroups))),
            new Progress<PublicStashFetchProgress>(item => progress?.Report(
                new TrackerProgress($"Загружаю предметы: пачка {item.CurrentBatch}/{item.TotalBatches}.", item.CurrentBatch, item.TotalBatches))),
            cancellationToken);

        var names = markers.Select(marker => marker.TabName).ToHashSet(StringComparer.Ordinal);
        var selected = discovery.Items
            .Where(item => names.Contains(item.TabName))
            .GroupBy(item => item.Id ?? $"{item.TabName}\u001f{item.X}\u001f{item.Y}\u001f{item.ItemName}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var total = 0m;
        var unpriced = 0;
        foreach (var group in selected.GroupBy(item => item.ItemName, StringComparer.Ordinal))
        {
            if (prices is null || !prices.DivinePrices.TryGetValue(PoeNinjaPriceClient.NormalizeItemName(group.Key), out var unitPrice))
            {
                unpriced++;
                continue;
            }

            total += unitPrice * group.Sum(item => item.StackSize);
        }

        var complete = !discovery.IsTruncated && markers.All(marker =>
            selected.Any(item => string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal)) &&
            !discovery.Items.Any(item => string.Equals(item.MarkerLabel, marker.Label, StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal)));
        var summary = complete
            ? $"Публичные вкладки: {selected.Length} стаков, оценка обновлена."
            : "Публичные вкладки прочитаны частично: проверьте предупреждения и названия вкладок.";
        return new PublicTabsValuation(total, unpriced, complete, DateTimeOffset.UtcNow, summary);
    }
}
