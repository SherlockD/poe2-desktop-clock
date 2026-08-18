using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DesktopClock.Infrastructure.Storage.Settings;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Regions;

namespace Poe2DesktopClock.Infrastructure.Windows.Runtime;

/// <summary>
/// Windows adapter for persisted settings and the interactive Currency setup.
/// Refresh and monitoring are application use cases and deliberately live elsewhere.
/// </summary>
public sealed class DesktopClockRuntime : ITrackerSettingsUseCase, ICurrencySetupUseCase, IAsyncDisposable
{
    private readonly WindowsGraphicsCaptureService _capture;
    private readonly bool _ownsCapture;
    private readonly CurrencySetupService _currencySetup;
    private readonly TrackerSettingsService _settings;
    private int _disposeStarted;

    public DesktopClockRuntime()
        : this(
            new PoeProcessLocator(),
            new WindowsGraphicsCaptureService(),
            CreateRegionStore(),
            CreateLayoutStore(),
            ownsCapture: true,
            publicStashSettingsStore: null)
    {
    }

    public DesktopClockRuntime(PoeProcessLocator processLocator, WindowsGraphicsCaptureService capture)
        : this(
            processLocator,
            capture,
            CreateRegionStore(),
            CreateLayoutStore(),
            ownsCapture: false,
            publicStashSettingsStore: null)
    {
    }

    public DesktopClockRuntime(
        PoeProcessLocator processLocator,
        WindowsGraphicsCaptureService capture,
        RegionStore regionStore,
        CurrencyLayoutStore layoutStore)
        : this(
            processLocator,
            capture,
            regionStore,
            layoutStore,
            ownsCapture: false,
            publicStashSettingsStore: null)
    {
    }

    /// <summary>
    /// Production composition shares the public-tab store with setup and
    /// background valuation, so a later settings save cannot restore markers
    /// that the initial setup explicitly excluded.
    /// </summary>
    public DesktopClockRuntime(
        PoeProcessLocator processLocator,
        WindowsGraphicsCaptureService capture,
        RegionStore regionStore,
        CurrencyLayoutStore layoutStore,
        PublicStashSettingsStore publicStashSettingsStore)
        : this(
            processLocator,
            capture,
            regionStore,
            layoutStore,
            ownsCapture: false,
            publicStashSettingsStore: publicStashSettingsStore)
    {
    }

    private DesktopClockRuntime(
        PoeProcessLocator processLocator,
        WindowsGraphicsCaptureService capture,
        RegionStore regionStore,
        CurrencyLayoutStore layoutStore,
        bool ownsCapture,
        PublicStashSettingsStore? publicStashSettingsStore)
    {
        _capture = capture;
        _ownsCapture = ownsCapture;
        var legacyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Poe2DeskTracker");
        var desktopDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Poe2DesktopClock");
        var publicTabStore = publicStashSettingsStore ?? new PublicStashSettingsStore(
            Path.Combine(legacyDirectory, "public-stash.json"));
        _settings = new TrackerSettingsService(
            new DesktopSettingsStore(Path.Combine(desktopDirectory, "settings.json")),
            publicTabStore);
        _currencySetup = new CurrencySetupService(
            processLocator,
            capture,
            regionStore,
            layoutStore,
            Path.Combine(desktopDirectory, "cache", "currency-preview.png"));
    }

    public TrackerSettings GetSettings() => _settings.Get();

    public void SaveSettings(TrackerSettings settings) => _settings.Save(settings);

    public CurrencySetupStatus GetCurrencySetupStatus() => _currencySetup.GetStatus();

    public Task SelectCurrencyRegionAsync(CancellationToken cancellationToken = default) =>
        _currencySetup.SelectRegionAsync(cancellationToken);

    public Task CalibrateCurrencySlotsAsync(CancellationToken cancellationToken = default) =>
        _currencySetup.CalibrateAsync(cancellationToken);

    /// <summary>Clears the desktop and legacy stores used for tracker setup.</summary>
    public void ClearPersistedConfiguration()
    {
        _currencySetup.Clear();
        _settings.Clear();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 0 && _ownsCapture)
        {
            _capture.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static RegionStore CreateRegionStore() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DeskTracker",
            "regions.json"));

    private static CurrencyLayoutStore CreateLayoutStore() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DeskTracker",
            "currency-layouts.json"));
}
