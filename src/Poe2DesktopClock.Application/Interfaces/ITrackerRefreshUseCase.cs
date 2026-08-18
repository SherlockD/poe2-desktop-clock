using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ITrackerRefreshUseCase
{
    event EventHandler<ClockSnapshot>? ClockSnapshotChanged;

    Task<ClockSnapshot> RefreshCurrencyAsync(
        CurrencyTabFrame frame,
        CancellationToken cancellationToken = default);

    Task<ClockSnapshot> RefreshPublicTabsAsync(
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
