using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ITrackerSettingsUseCase
{
    TrackerSettings GetSettings();

    void SaveSettings(TrackerSettings settings);
}
