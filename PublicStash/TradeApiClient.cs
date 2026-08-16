using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Poe2DeskTracker.PublicStash;

internal sealed class TradeApiClient : IDisposable
{
    private const int FetchBatchSize = 10;
    private const int FetchBatchDelayMilliseconds = 400;
    // The Trade search policy also has a rolling five-minute budget. Spacing
    // the configured tab-marker queries avoids bursts, rather than only
    // satisfying the short 5-per-10-second rule.
    private const int SearchGroupDelayMilliseconds = 10_100;
    private const int MaximumRateLimitRetries = 2;
    private static readonly Uri BaseUri = new("https://www.pathofexile.com/");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;

    internal TradeApiClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Poe2DeskTracker", "0.1"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal async Task<IReadOnlyList<string>> GetPoe2LeagueNamesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRateLimitRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "api/trade2/data/leagues"),
            cancellationToken);
        var payload = await ReadSuccessPayloadAsync(response, cancellationToken);
        var leagues = JsonSerializer.Deserialize<TradeLeagueResponse>(payload, JsonOptions)?.Result ?? [];

        return leagues
            .Where(league => string.Equals(league.Realm, "poe2", StringComparison.OrdinalIgnoreCase))
            .Select(league => league.Id)
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Reads configured public tabs through their unique marker prices. Trade
    /// cannot search by stash name; querying an exact tab price avoids making
    /// assumptions about an item's trade category or English item name.
    /// </summary>
    internal async Task<PublicStashDiscovery> DiscoverPublicTabItemsAsync(
        string accountName,
        string league,
        IReadOnlyList<PublicStashTabMarker> tabMarkers,
        IProgress<PublicStashSearchProgress>? progress = null,
        IProgress<PublicStashFetchProgress>? fetchProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(league);
        ArgumentNullException.ThrowIfNull(tabMarkers);
        if (tabMarkers.Count == 0)
        {
            throw new ArgumentException("Configure at least one public tab marker.", nameof(tabMarkers));
        }

        var groupResults = new List<PublicStashSearchGroupResult>(tabMarkers.Count);
        var itemIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var markerLabelsByQueryId = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var groupIndex = 0; groupIndex < tabMarkers.Count; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marker = tabMarkers[groupIndex];
            var search = await SearchPublicTabByMarkerAsync(accountName, league, marker, cancellationToken);
            var resultIds = search.Result ?? [];
            groupResults.Add(new PublicStashSearchGroupResult(marker.Label, marker.PriceAmount, marker.PriceCurrency, search.Total, resultIds.Count));
            progress?.Report(new PublicStashSearchProgress(
                groupIndex + 1,
                tabMarkers.Count,
                marker.Label,
                marker.PriceAmount,
                marker.PriceCurrency,
                search.Total,
                resultIds.Count));
            foreach (var id in resultIds)
            {
                itemIds.TryAdd(id, search.Id!);
            }
            markerLabelsByQueryId[search.Id!] = marker.Label;

            if (groupIndex + 1 < tabMarkers.Count)
            {
                await Task.Delay(SearchGroupDelayMilliseconds, cancellationToken);
            }
        }

        var fetchBatches = itemIds
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .SelectMany(group => group
                .Select(pair => pair.Key)
                .Chunk(FetchBatchSize)
                .Select(ids => new FetchBatch(group.Key, ids)))
            .ToArray();
        var items = new List<PublicStashItem>(itemIds.Count);
        for (var batchIndex = 0; batchIndex < fetchBatches.Length; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = fetchBatches[batchIndex];
            fetchProgress?.Report(new PublicStashFetchProgress(
                batchIndex + 1,
                fetchBatches.Length,
                batch.ItemIds.Length));
            var fetchUri = $"api/trade2/fetch/{string.Join(',', batch.ItemIds)}?query={Uri.EscapeDataString(batch.QueryId)}";
            using var fetchResponse = await SendWithRateLimitRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, fetchUri),
                cancellationToken);
            var fetchPayload = await ReadSuccessPayloadAsync(fetchResponse, cancellationToken);
            var fetched = JsonSerializer.Deserialize<TradeFetchResponse>(fetchPayload, JsonOptions)?.Result ?? [];

            foreach (var result in fetched)
            {
                var stashName = result.Listing?.Stash?.Name;
                var typeLine = result.Item?.TypeLine ?? result.Item?.BaseType;
                if (string.IsNullOrWhiteSpace(stashName) || string.IsNullOrWhiteSpace(typeLine))
                {
                    continue;
                }

                items.Add(new PublicStashItem(
                    result.Item?.Id ?? result.Id,
                    stashName,
                    typeLine,
                    Math.Max(1, result.Item?.StackSize ?? 1),
                    result.Listing?.Stash?.X,
                    result.Listing?.Stash?.Y,
                    markerLabelsByQueryId[batch.QueryId]));
            }

            if (batchIndex + 1 < fetchBatches.Length)
            {
                await Task.Delay(FetchBatchDelayMilliseconds, cancellationToken);
            }
        }

        return new PublicStashDiscovery(groupResults, itemIds.Count, items);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<TradeSearchResponse> SearchPublicTabByMarkerAsync(
        string accountName,
        string league,
        PublicStashTabMarker marker,
        CancellationToken cancellationToken)
    {
        var searchRequest = new
        {
            query = new
            {
                status = new { option = "any" },
                stats = Array.Empty<object>(),
                filters = new
                {
                    trade_filters = new
                    {
                        filters = new
                        {
                            account = new { input = accountName.Trim() },
                            price = new
                            {
                                min = marker.PriceAmount,
                                max = marker.PriceAmount,
                                option = marker.PriceCurrency,
                            },
                        },
                    },
                },
            },
            sort = new { price = "asc" },
        };
        var serializedRequest = JsonSerializer.Serialize(searchRequest, JsonOptions);
        var searchUri = $"api/trade2/search/poe2/{Uri.EscapeDataString(league.Trim())}";
        using var response = await SendWithRateLimitRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, searchUri)
            {
                Content = new StringContent(serializedRequest, Encoding.UTF8, "application/json"),
            },
            cancellationToken);
        var payload = await ReadSuccessPayloadAsync(response, cancellationToken);
        var search = JsonSerializer.Deserialize<TradeSearchResponse>(payload, JsonOptions)
            ?? throw new TradeApiException("The trade API returned an unreadable search response.");
        if (string.IsNullOrWhiteSpace(search.Id))
        {
            throw new TradeApiException("The trade API search response did not include a query id.");
        }

        return search;
    }

    private async Task<HttpResponseMessage> SendWithRateLimitRetryAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = createRequest();
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaximumRateLimitRetries)
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 + attempt * 2);
            response.Dispose();
            await Task.Delay(retryAfter, cancellationToken);
        }
    }

    private static async Task<string> ReadSuccessPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return payload;
        }

        var detail = payload.Length > 500 ? $"{payload[..500]}…" : payload;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TradeApiException("Trade API rate limit was reached. Try again in a moment.");
        }

        throw new TradeApiException($"Trade API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }

    private sealed record TradeLeagueResponse(List<TradeLeague>? Result);

    private sealed record TradeLeague(string? Id, string? Realm);

    private sealed record TradeSearchResponse(string? Id, int Total, List<string>? Result);

    private sealed record TradeFetchResponse(List<TradeFetchResult>? Result);

    private sealed record TradeFetchResult(string Id, TradeListing? Listing, TradeItem? Item);

    private sealed record TradeListing(TradeStash? Stash);

    private sealed record TradeStash(string? Name, int? X, int? Y);

    private sealed record TradeItem(string? Id, string? TypeLine, string? BaseType, long? StackSize);

    private sealed record FetchBatch(string QueryId, string[] ItemIds);
}

internal sealed class TradeApiException(string message) : Exception(message);

internal sealed record PublicStashItem(
    string? Id,
    string TabName,
    string ItemName,
    long StackSize,
    int? X,
    int? Y,
    string MarkerLabel);

internal sealed record PublicStashDiscovery(
    IReadOnlyList<PublicStashSearchGroupResult> SearchGroups,
    int ReturnedUniqueItemIds,
    IReadOnlyList<PublicStashItem> Items)
{
    internal bool IsTruncated => SearchGroups.Any(group => group.IsTruncated);
}

internal sealed record PublicStashSearchGroupResult(
    string Label,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    int ReturnedItemIds)
{
    internal bool IsTruncated => TotalMatches > ReturnedItemIds;
}

internal sealed record PublicStashSearchProgress(
    int CompletedGroups,
    int TotalGroups,
    string Label,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    int ReturnedItemIds);

internal sealed record PublicStashFetchProgress(
    int CurrentBatch,
    int TotalBatches,
    int ItemCount);
