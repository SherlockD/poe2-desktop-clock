using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Infrastructure.Storage.Onboarding;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DesktopClock.Infrastructure.Storage.Snapshots;

namespace Poe2DesktopClock.Infrastructure.Windows.Runtime;

/// <summary>
/// Clears all data files owned by the desktop tracker. It intentionally targets
/// only known configuration and cache files, never their parent directories.
/// </summary>
public sealed class WindowsApplicationDataResetter : IApplicationDataResetter
{
    private readonly DesktopClockRuntime _runtime;
    private readonly InitialSetupStateStore _initialSetupState;
    private readonly LastClockSnapshotStore _lastClockSnapshot;
    private readonly PublicTabsSnapshotStore _publicTabsSnapshot;

    public WindowsApplicationDataResetter(
        DesktopClockRuntime runtime,
        InitialSetupStateStore initialSetupState,
        LastClockSnapshotStore lastClockSnapshot,
        PublicTabsSnapshotStore publicTabsSnapshot)
    {
        _runtime = runtime;
        _initialSetupState = initialSetupState;
        _lastClockSnapshot = lastClockSnapshot;
        _publicTabsSnapshot = publicTabsSnapshot;
    }

    public void Reset()
    {
        _runtime.ClearPersistedConfiguration();
        DeleteCurrencyLiveFrame();
        _publicTabsSnapshot.Clear();
        _lastClockSnapshot.Clear();
        _initialSetupState.Clear();
    }

    private static void DeleteCurrencyLiveFrame()
    {
        var liveFramePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DesktopClock",
            "cache",
            "currency-live.png");
        if (File.Exists(liveFramePath))
        {
            File.Delete(liveFramePath);
        }
    }
}
