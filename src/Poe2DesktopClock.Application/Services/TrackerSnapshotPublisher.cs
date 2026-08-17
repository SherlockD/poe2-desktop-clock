using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

public sealed class TrackerSnapshotPublisher : ITrackerSnapshotPublisher
{
    public event EventHandler<ClockSnapshot>? ClockSnapshotChanged;

    public void Publish(ClockSnapshot snapshot) => ClockSnapshotChanged?.Invoke(this, snapshot);
}
