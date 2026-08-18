using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>Trade API implementation of the public-tabs setup gateway.</summary>
public sealed class TradeApiPublicTabsSetupGateway : IPublicTabsSetupTradeGateway
{
    private readonly TradeApiClient _tradeApi;

    public TradeApiPublicTabsSetupGateway(TradeApiClient tradeApi)
    {
        ArgumentNullException.ThrowIfNull(tradeApi);
        _tradeApi = tradeApi;
    }

    public async Task<PublicStashSearchResult> SearchAsync(
        string accountName,
        string league,
        PublicStashTabMarker marker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marker);
        var searches = await _tradeApi.SearchPublicTabItemsAsync(
            accountName,
            league,
            [marker],
            cancellationToken: cancellationToken);
        return searches.SingleOrDefault()
            ?? throw new TradeApiException("Trade API did not return a marker search result.");
    }

    public Task<IReadOnlyList<PublicStashItem>> FetchAsync(
        PublicStashSearchResult search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        return _tradeApi.FetchPublicTabItemsAsync([search], cancellationToken: cancellationToken);
    }
}
