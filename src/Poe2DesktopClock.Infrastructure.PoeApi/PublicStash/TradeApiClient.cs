using System.Net;
using System.Text;
using System.Text.Json;
using Poe2DesktopClock.Application.Interfaces;

namespace Poe2DeskTracker.PublicStash;

public sealed class TradeApiClient : ILeagueCatalog
{
    public const string HttpClientName = "Poe2TradeApi";

    private const int FetchBatchSize = 10;
    private const int MaximumRateLimitRetries = 2;
    private const string SearchRateLimitPolicy = "trade-search-request-limit";
    private const string FetchRateLimitPolicy = "trade-fetch-request-limit";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TradeRequestRateLimiter _rateLimiter = new();

    public TradeApiClient(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<string>> GetPoe2LeaguesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRateLimitRetryAsync(
            FetchRateLimitPolicy,
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
    public async Task<PublicStashDiscovery> DiscoverPublicTabItemsAsync(
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

        var searches = await SearchPublicTabItemsAsync(
            accountName,
            league,
            tabMarkers,
            progress,
            cancellationToken);
        var items = await FetchPublicTabItemsAsync(searches, fetchProgress, cancellationToken);

        return new PublicStashDiscovery(
            searches.Select(search => search.ToSearchGroupResult()).ToArray(),
            searches.SelectMany(search => search.ItemIds).Distinct(StringComparer.Ordinal).Count(),
            items);
    }

    /// <summary>
    /// Searches each configured marker and returns only item identities. The
    /// caller can compare this light-weight result with a stored snapshot
    /// before deciding which groups need a fetch request.
    /// </summary>
    public async Task<IReadOnlyList<PublicStashSearchResult>> SearchPublicTabItemsAsync(
        string accountName,
        string league,
        IReadOnlyList<PublicStashTabMarker> tabMarkers,
        IProgress<PublicStashSearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(league);
        ArgumentNullException.ThrowIfNull(tabMarkers);
        if (tabMarkers.Count == 0)
        {
            throw new ArgumentException("Configure at least one public tab marker.", nameof(tabMarkers));
        }

        var searches = new List<PublicStashSearchResult>(tabMarkers.Count);
        for (var groupIndex = 0; groupIndex < tabMarkers.Count; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marker = tabMarkers[groupIndex];
            var search = await SearchPublicTabByMarkerAsync(accountName, league, marker, cancellationToken);
            var resultIds = search.Result ?? [];
            searches.Add(new PublicStashSearchResult(
                marker.Label,
                marker.PriceAmount,
                marker.PriceCurrency,
                search.Total,
                search.Id!,
                resultIds));
            progress?.Report(new PublicStashSearchProgress(
                groupIndex + 1,
                tabMarkers.Count,
                marker.Label,
                marker.PriceAmount,
                marker.PriceCurrency,
                search.Total,
                resultIds.Count));
        }

        return searches;
    }

    /// <summary>Fetches full item data for previously searched marker groups.</summary>
    public async Task<IReadOnlyList<PublicStashItem>> FetchPublicTabItemsAsync(
        IReadOnlyList<PublicStashSearchResult> searches,
        IProgress<PublicStashFetchProgress>? fetchProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searches);
        var itemQueries = new Dictionary<string, PublicStashSearchResult>(StringComparer.Ordinal);
        foreach (var search in searches)
        {
            foreach (var itemId in search.ItemIds)
            {
                itemQueries.TryAdd(itemId, search);
            }
        }

        var fetchBatches = itemQueries
            .GroupBy(pair => pair.Value.QueryId, StringComparer.Ordinal)
            .SelectMany(group => group
                .Select(pair => pair.Key)
                .Chunk(FetchBatchSize)
                .Select(ids => new FetchBatch(group.Key, group.First().Value.Label, ids)))
            .ToArray();
        var items = new List<PublicStashItem>(itemQueries.Count);
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
                FetchRateLimitPolicy,
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
                    batch.MarkerLabel));
            }
        }

        return items;
    }

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
            SearchRateLimitPolicy,
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
        string rateLimitPolicy,
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await _rateLimiter.WaitAsync(rateLimitPolicy, cancellationToken);
            using var request = createRequest();
            var response = await _httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _rateLimiter.Observe(rateLimitPolicy, response.Headers);
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

    private sealed record FetchBatch(string QueryId, string MarkerLabel, string[] ItemIds);
}
