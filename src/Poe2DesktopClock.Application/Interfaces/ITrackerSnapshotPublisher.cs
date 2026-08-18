using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>Единый канал публикации снимков Currency и публичных вкладок.</summary>
public interface ITrackerSnapshotPublisher
{
    event EventHandler<ClockSnapshot>? ClockSnapshotChanged;

    void Publish(ClockSnapshot snapshot);
}
