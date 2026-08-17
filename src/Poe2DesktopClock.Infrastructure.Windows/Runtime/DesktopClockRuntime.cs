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

    public DesktopClockRuntime()
        : this(new PoeProcessLocator(), new WindowsGraphicsCaptureService(), ownsCapture: true)
    {
    }

    public DesktopClockRuntime(PoeProcessLocator processLocator, WindowsGraphicsCaptureService capture)
        : this(processLocator, capture, ownsCapture: false)
    {
    }

    private DesktopClockRuntime(PoeProcessLocator processLocator, WindowsGraphicsCaptureService capture, bool ownsCapture)
    {
        _capture = capture;
        _ownsCapture = ownsCapture;
        var legacyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Poe2DeskTracker");
        var desktopDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Poe2DesktopClock");
        var regionStore = new RegionStore(Path.Combine(legacyDirectory, "regions.json"));
        var layoutStore = new CurrencyLayoutStore(Path.Combine(legacyDirectory, "currency-layouts.json"));
        var publicTabStore = new PublicStashSettingsStore(Path.Combine(legacyDirectory, "public-stash.json"));
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

    public ValueTask DisposeAsync()
    {
        if (_ownsCapture)
        {
            _capture.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
