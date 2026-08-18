using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Provides the last successfully calculated tracker snapshot across application restarts.
/// </summary>
public interface ILastClockSnapshotStore
{
    ClockSnapshot? GetLastSnapshot();

    void Save(ClockSnapshot snapshot);
}
