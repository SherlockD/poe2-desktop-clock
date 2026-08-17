using Poe2DeskTracker.PublicStash;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DesktopClock.Infrastructure.Storage.Settings;

namespace Poe2DesktopClock.Infrastructure.Windows.Runtime;

/// <summary>
/// Owns the transition between the legacy public-tab configuration and the
/// desktop tracker settings file. Keeping it here prevents refresh and monitor
/// workflows from deciding which store owns a setting.
/// </summary>
internal sealed class TrackerSettingsService
{
    private readonly DesktopSettingsStore _desktopSettingsStore;
    private readonly PublicStashSettingsStore _publicStashSettingsStore;

    internal TrackerSettingsService(
        DesktopSettingsStore desktopSettingsStore,
        PublicStashSettingsStore publicStashSettingsStore)
    {
        _desktopSettingsStore = desktopSettingsStore;
        _publicStashSettingsStore = publicStashSettingsStore;
    }

    internal TrackerSettings Get()
    {
        var publicSettings = _publicStashSettingsStore.Get();
        var fallback = TrackerSettings.Default with
        {
            AccountName = publicSettings?.AccountName ?? string.Empty,
            League = publicSettings?.League ?? string.Empty,
        };
        return _desktopSettingsStore.Get(fallback);
    }

    internal void Save(TrackerSettings settings)
    {
        var normalized = settings.Normalize();
        _desktopSettingsStore.Save(normalized);

        var existing = _publicStashSettingsStore.Get();
        var markers = existing is { HasCompleteMarkers: true }
            ? existing.TabMarkers!
            : PublicTabMarkerCatalog.CreateDefaultMarkers().ToList();
        _publicStashSettingsStore.Save(new PublicStashSettings(
            normalized.AccountName,
            normalized.League,
            [],
            new List<PublicStashTabMarker>(markers)));
    }
}
