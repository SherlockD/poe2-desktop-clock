namespace Poe2DesktopClock.Application.Models;

/// <summary>
/// The account, league, and explicitly selected public tabs to verify through
/// the Trade API. Unselected tabs remain visible in the result but are never
/// sent to the API.
/// </summary>
public sealed record PublicTabsSetupRequest(
    string AccountName,
    string League,
    IReadOnlyList<PublicTabsSetupTab> Tabs);
