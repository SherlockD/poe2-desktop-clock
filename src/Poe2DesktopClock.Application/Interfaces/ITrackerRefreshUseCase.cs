using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ITrackerRefreshUseCase
{
    event EventHandler<ClockSnapshot>? ClockSnapshotChanged;

    Task<ClockSnapshot> RefreshAsync(
        bool refreshPublicTabs,
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
