using System.Net;
using System.Text;
using Poe2DeskTracker.Pricing;
using Xunit;

namespace Poe2DesktopClock.Contracts.Tests;

public sealed class PoeNinjaPriceClientTests
{
    [Fact]
    public async Task GetPricesAsync_loads_idol_prices()
    {
        var handler = new PriceOverviewHandler();
        var client = new PoeNinjaPriceClient(new TestHttpClientFactory(handler));

        var snapshot = await client.GetPricesAsync("League", TimeSpan.Zero);

        Assert.Contains("Idols", handler.RequestedTypes);
        Assert.True(snapshot.TryGetDivinePrice("Idol of Egrin", out var price));
        Assert.Equal(0.05m, price);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpMessageHandler handler) =>
            _client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class PriceOverviewHandler : HttpMessageHandler
    {
        public List<string> RequestedTypes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var type = request.RequestUri!.Query
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Single(part => part.StartsWith("type=", StringComparison.Ordinal))
                ["type=".Length..];
            RequestedTypes.Add(Uri.UnescapeDataString(type));

            var payload = string.Equals(type, "Idols", StringComparison.Ordinal)
                ? """{"core":{"primary":"divine"},"items":[{"id":"idol-egrin","name":"Idol of Egrin"}],"lines":[{"id":"idol-egrin","primaryValue":0.05}]}"""
                : """{"core":{"primary":"divine"},"items":[],"lines":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
