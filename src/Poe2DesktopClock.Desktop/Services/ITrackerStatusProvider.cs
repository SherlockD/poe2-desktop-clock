using Poe2DesktopClock.Desktop.Models;

namespace Poe2DesktopClock.Desktop.Services;

public interface ITrackerStatusProvider
{
    event EventHandler<TrackerStatusSnapshot>? StatusChanged;

    TrackerStatusSnapshot GetCurrent();

    Task InitializeAsync();
}
