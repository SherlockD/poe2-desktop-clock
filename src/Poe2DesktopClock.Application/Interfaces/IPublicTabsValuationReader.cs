using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface IPublicTabsValuationReader
{
    Task<PublicTabsValuation> ReadAsync(
        TrackerSettings settings,
        PriceSnapshot? prices,
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
