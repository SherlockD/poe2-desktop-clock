using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface IPublicTabsRefreshUseCase
{
    event EventHandler<PublicTabsRefreshResult>? Refreshed;

    Task<PublicTabsRefreshResult> RefreshAsync(
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
