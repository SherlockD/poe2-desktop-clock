using System.Net;
using System.Text;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DeskTracker.PublicStash;
using Xunit;

namespace Poe2DesktopClock.Contracts.Tests;

public sealed class PublicTabsValuationReaderTests
{
    [Fact]
    public async Task ReadAsync_reports_items_without_prices()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"poe2-clock-tests-{Guid.NewGuid():N}");
        try
        {
            var reader = new PublicTabsValuationReader(
                new TradeApiClient(new TestHttpClientFactory(new PublicTabTradeHandler())),
                new SingleMarkerProvider(),
                new PublicTabsSnapshotStore(Path.Combine(directory, "public-tabs-snapshot.json")));
            var settings = TrackerSettings.Default with { AccountName = "account", League = "League" };
            var prices = new PriceSnapshot(
                DateTimeOffset.UtcNow,
                new Dictionary<string, decimal>(StringComparer.Ordinal));

            var valuation = await reader.ReadAsync(settings, prices);

            Assert.Equal(0m, valuation.Divines);
            Assert.Equal(1, valuation.UnpricedItems);
            Assert.True(valuation.IsComplete);
            Assert.Contains("без цены", valuation.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_skips_fetch_when_marker_search_has_not_changed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"poe2-clock-tests-{Guid.NewGuid():N}");
        try
        {
            var snapshotPath = Path.Combine(directory, "public-tabs-snapshot.json");
            var handler = new PublicTabTradeHandler();
            var client = new TradeApiClient(new TestHttpClientFactory(handler));
            var reader = new PublicTabsValuationReader(
                client,
                new SingleMarkerProvider(),
                new PublicTabsSnapshotStore(snapshotPath));
            var settings = TrackerSettings.Default with { AccountName = "account", League = "League" };
            var prices = new PriceSnapshot(
                DateTimeOffset.UtcNow,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["ORB"] = 2m });

            var first = await reader.ReadAsync(settings, prices);
            var second = await reader.ReadAsync(settings, prices);
            var afterPriceChange = await reader.ReadAsync(
                settings,
                prices with
                {
                    RetrievedAt = prices.RetrievedAt.AddMinutes(30),
                    DivinePrices = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["ORB"] = 3m },
                });

            Assert.Equal(6m, first.Divines);
            Assert.Equal(first, second);
            Assert.Equal(9m, afterPriceChange.Divines);
            Assert.Equal(3, handler.SearchRequests);
            Assert.Equal(1, handler.FetchRequests);
            Assert.Equal(9m, new PublicTabsSnapshotStore(snapshotPath).Get()!.Valuation!.Divines);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class SingleMarkerProvider : IPublicTabMarkerProvider
    {
        public IReadOnlyList<PublicTabMarker> GetMarkers() =>
        [
            new PublicTabMarker("Тест", "~price 1001 mirror", 1001m, "mirror"),
        ];
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class PublicTabTradeHandler : HttpMessageHandler
    {
        public int SearchRequests { get; private set; }

        public int FetchRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("/search/", StringComparison.Ordinal) == true)
            {
                SearchRequests++;
                return Task.FromResult(CreateResponse("""{"id":"query-1","total":1,"result":["item-1"]}""", "trade-search-request-limit"));
            }

            if (request.RequestUri?.AbsolutePath.Contains("/fetch/", StringComparison.Ordinal) == true)
            {
                FetchRequests++;
                return Task.FromResult(CreateResponse("""
                    {"result":[{"id":"item-1","listing":{"stash":{"name":"~price 1001 mirror","x":0,"y":0}},"item":{"id":"item-1","typeLine":"Orb","stackSize":3}}]}
                    """, "trade-fetch-request-limit"));
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }

        private static HttpResponseMessage CreateResponse(string json, string policy)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("X-Rate-Limit-Policy", policy);
            response.Headers.Add("X-Rate-Limit-Rules", "client");
            response.Headers.Add("X-Rate-Limit-Client", "10:60:10");
            response.Headers.Add("X-Rate-Limit-Client-State", "1:60:0");
            return response;
        }
    }
}
