using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>
/// Small seam around Trade API operations used only while configuring public
/// tabs. Keeping this boundary narrow lets setup classification be tested
/// without HTTP or a real Path of Exile account.
/// </summary>
public interface IPublicTabsSetupTradeGateway
{
    Task<PublicStashSearchResult> SearchAsync(
        string accountName,
        string league,
        PublicStashTabMarker marker,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicStashItem>> FetchAsync(
        PublicStashSearchResult search,
        CancellationToken cancellationToken = default);
}
