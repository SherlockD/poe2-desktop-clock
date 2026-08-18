using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Domain.Tracking;
using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.PublicStash;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>
/// Reads public tabs through Trade API. Search results are compared with a
/// persisted snapshot so that unchanged marker groups do not need a fetch.
/// </summary>
public sealed class PublicTabsValuationReader : IPublicTabsValuationReader
{
    private static readonly TimeSpan FullFetchInterval = TimeSpan.FromMinutes(15);
    private readonly IPublicTabMarkerProvider _markers;
    private readonly TradeApiClient _tradeApi;
    private readonly PublicTabsSnapshotStore _snapshots;

    public PublicTabsValuationReader(
        TradeApiClient tradeApi,
        IPublicTabMarkerProvider markers,
        PublicTabsSnapshotStore snapshots)
    {
        ArgumentNullException.ThrowIfNull(tradeApi);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(snapshots);
        _tradeApi = tradeApi;
        _markers = markers;
        _snapshots = snapshots;
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
        var cached = GetCompatibleSnapshot(settings, markers);
        var now = DateTimeOffset.UtcNow;
        var requiresFullFetch = cached is null || now - cached.LastFullFetchAt >= FullFetchInterval;
        var searches = await _tradeApi.SearchPublicTabItemsAsync(
            settings.AccountName,
            settings.League,
            markers,
            new Progress<PublicStashSearchProgress>(item => progress?.Report(
                new TrackerProgress($"Публичные вкладки: {item.CompletedGroups}/{item.TotalGroups} — {item.Label}.", item.CompletedGroups, item.TotalGroups))),
            cancellationToken);

        var cachedByLabel = cached?.Markers.ToDictionary(marker => marker.Label, StringComparer.Ordinal)
            ?? new Dictionary<string, StoredPublicTabMarkerSnapshot>(StringComparer.Ordinal);
        var searchesToFetch = searches
            .Where(search => requiresFullFetch ||
                             !cachedByLabel.TryGetValue(search.Label, out var previous) ||
                             !HasSameSearchResult(previous, search))
            .ToArray();
        var fetchedItems = await _tradeApi.FetchPublicTabItemsAsync(
            searchesToFetch,
            new Progress<PublicStashFetchProgress>(item => progress?.Report(
                new TrackerProgress($"Загружаю предметы: пачка {item.CurrentBatch}/{item.TotalBatches}.", item.CurrentBatch, item.TotalBatches))),
            cancellationToken);
        var fetchedByLabel = searchesToFetch.ToDictionary(
            search => search.Label,
            _ => (IReadOnlyList<PublicStashItem>)Array.Empty<PublicStashItem>(),
            StringComparer.Ordinal);
        foreach (var group in fetchedItems.GroupBy(item => item.MarkerLabel, StringComparer.Ordinal))
        {
            fetchedByLabel[group.Key] = group.ToArray();
        }

        var currentMarkers = searches
            .Select(search => CreateMarkerSnapshot(search, markers, cachedByLabel, fetchedByLabel))
            .ToArray();

        var names = markers.Select(marker => marker.TabName).ToHashSet(StringComparer.Ordinal);
        var allItems = currentMarkers.SelectMany(marker => marker.Items).ToArray();
        var selected = allItems
            .Where(item => names.Contains(item.TabName))
            .GroupBy(item => item.Id ?? $"{item.TabName}\u001f{item.X}\u001f{item.Y}\u001f{item.ItemName}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var tabs = markers
            .Select(marker => CalculateTabValuation(
                marker,
                searches.Single(search => string.Equals(search.Label, marker.Label, StringComparison.Ordinal)),
                selected,
                allItems,
                prices))
            .ToArray();
        var complete = tabs.All(tab => tab.IsComplete);
        var inventoryFingerprint = CreateInventoryFingerprint(currentMarkers);
        var priceFingerprint = CreatePriceFingerprint(selected, prices);
        var previousValuation = cached?.Valuation;
        var canReuseValuation = cached is not null && previousValuation is not null &&
                                string.Equals(cached.InventoryFingerprint, inventoryFingerprint, StringComparison.Ordinal) &&
                                string.Equals(cached.PriceFingerprint, priceFingerprint, StringComparison.Ordinal) &&
                                previousValuation.IsComplete == complete &&
                                HasTabValuationsForMarkers(previousValuation, markers);
        var valuation = canReuseValuation
            ? previousValuation!
            : CalculateValuation(selected, prices, complete, now) with { Tabs = tabs };

        _snapshots.Save(new StoredPublicTabsSnapshot(
            settings.AccountName.Trim(),
            settings.League.Trim(),
            currentMarkers,
            requiresFullFetch ? now : cached?.LastFullFetchAt ?? now,
            inventoryFingerprint,
            priceFingerprint,
            valuation));
        return valuation;
    }

    private StoredPublicTabsSnapshot? GetCompatibleSnapshot(
        TrackerSettings settings,
        IReadOnlyList<PublicStashTabMarker> markers)
    {
        var snapshot = _snapshots.Get();
        if (snapshot is null ||
            !string.Equals(snapshot.AccountName, settings.AccountName.Trim(), StringComparison.Ordinal) ||
            !string.Equals(snapshot.League, settings.League.Trim(), StringComparison.Ordinal) ||
            snapshot.Markers.Count != markers.Count)
        {
            return null;
        }

        return markers.All(marker => snapshot.Markers.Any(stored => HasSameMarker(stored, marker)))
            ? snapshot
            : null;
    }

    private static StoredPublicTabMarkerSnapshot CreateMarkerSnapshot(
        PublicStashSearchResult search,
        IReadOnlyList<PublicStashTabMarker> configuredMarkers,
        IReadOnlyDictionary<string, StoredPublicTabMarkerSnapshot> cachedByLabel,
        IReadOnlyDictionary<string, IReadOnlyList<PublicStashItem>> fetchedByLabel)
    {
        var marker = configuredMarkers.Single(candidate => string.Equals(candidate.Label, search.Label, StringComparison.Ordinal));
        var items = fetchedByLabel.TryGetValue(search.Label, out var fetched)
            ? fetched
            : cachedByLabel[search.Label].Items;
        return new StoredPublicTabMarkerSnapshot(
            marker.Label,
            marker.TabName,
            marker.PriceAmount,
            marker.PriceCurrency,
            search.TotalMatches,
            search.ItemIds.ToArray(),
            items.ToArray());
    }

    private static bool HasSameMarker(StoredPublicTabMarkerSnapshot stored, PublicStashTabMarker marker) =>
        string.Equals(stored.Label, marker.Label, StringComparison.Ordinal) &&
        string.Equals(stored.TabName, marker.TabName, StringComparison.Ordinal) &&
        stored.PriceAmount == marker.PriceAmount &&
        string.Equals(stored.PriceCurrency, marker.PriceCurrency, StringComparison.OrdinalIgnoreCase);

    private static bool HasSameSearchResult(StoredPublicTabMarkerSnapshot previous, PublicStashSearchResult current) =>
        previous.TotalMatches == current.TotalMatches &&
        previous.ItemIds.Count == current.ItemIds.Count &&
        previous.ItemIds.ToHashSet(StringComparer.Ordinal).SetEquals(current.ItemIds);

    private static bool HasTabValuationsForMarkers(
        PublicTabsValuation valuation,
        IReadOnlyList<PublicStashTabMarker> markers) =>
        valuation.Tabs.Count == markers.Count &&
        markers.All(marker => valuation.Tabs.Any(tab =>
            string.Equals(tab.Label, marker.Label, StringComparison.Ordinal) &&
            string.Equals(tab.TabName, marker.TabName, StringComparison.Ordinal)));

    private static PublicTabValuation CalculateTabValuation(
        PublicStashTabMarker marker,
        PublicStashSearchResult search,
        IReadOnlyList<PublicStashItem> selected,
        IReadOnlyList<PublicStashItem> allItems,
        PriceSnapshot? prices)
    {
        var tabItems = selected
            .Where(item => string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal))
            .ToArray();
        var total = 0m;
        var unpriced = 0;
        foreach (var group in tabItems.GroupBy(item => item.ItemName, StringComparer.Ordinal))
        {
            if (prices is null || !prices.DivinePrices.TryGetValue(PoeNinjaPriceClient.NormalizeItemName(group.Key), out var unitPrice))
            {
                unpriced++;
                continue;
            }

            total += unitPrice * group.Sum(item => item.StackSize);
        }

        var hasOnlyExpectedTab = !allItems.Any(item =>
            string.Equals(item.MarkerLabel, marker.Label, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal));
        var isComplete = !search.IsTruncated && tabItems.Length > 0 && hasOnlyExpectedTab;
        return new PublicTabValuation(
            marker.Label,
            marker.TabName,
            total,
            search.TotalMatches,
            search.ItemIds.Count,
            tabItems.Length,
            unpriced,
            isComplete);
    }

    private static PublicTabsValuation CalculateValuation(
        IReadOnlyList<PublicStashItem> selected,
        PriceSnapshot? prices,
        bool complete,
        DateTimeOffset updatedAt)
    {
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

        var summary = !complete
            ? "Публичные вкладки прочитаны частично: проверьте предупреждения и названия вкладок."
            : unpriced > 0
                ? $"Публичные вкладки прочитаны не полностью по стоимости: типов предметов без цены — {unpriced}."
                : $"Публичные вкладки: {selected.Count} стаков, оценка обновлена.";
        return new PublicTabsValuation(total, unpriced, complete, updatedAt, summary);
    }

    private static string CreateInventoryFingerprint(IReadOnlyList<StoredPublicTabMarkerSnapshot> markers)
    {
        var canonical = string.Join('\n', markers
            .OrderBy(marker => marker.Label, StringComparer.Ordinal)
            .SelectMany(marker => marker.Items
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ThenBy(item => item.TabName, StringComparer.Ordinal)
                .ThenBy(item => item.X)
                .ThenBy(item => item.Y)
                .Select(item => string.Join('\u001f', marker.Label, item.Id, item.StashId, item.TabName, item.ItemName, item.StackSize, item.X, item.Y))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string CreatePriceFingerprint(IReadOnlyList<PublicStashItem> selected, PriceSnapshot? prices)
    {
        var canonical = string.Join('\n', selected
            .Select(item => PoeNinjaPriceClient.NormalizeItemName(item.ItemName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => prices is not null && prices.DivinePrices.TryGetValue(name, out var price)
                ? $"{name}={price.ToString(CultureInfo.InvariantCulture)}"
                : $"{name}=<missing>"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
