using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Regions;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

/// <summary>Detects Currency tab visibility and content changes through Windows capture.</summary>
public sealed class WindowsCurrencyChangeMonitor : ICurrencyChangeMonitor
{
    private const string CurrencyRegionName = "currency";
    private readonly PoeProcessLocator _processLocator;
    private readonly WindowsGraphicsCaptureService _capture;
    private readonly RegionStore _regions;
    private readonly CurrencyLayoutStore _layouts;
    private readonly string _liveFramePath;

    public WindowsCurrencyChangeMonitor(PoeProcessLocator processLocator, WindowsGraphicsCaptureService capture)
    {
        _processLocator = processLocator;
        _capture = capture;
        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DeskTracker");
        _regions = new RegionStore(Path.Combine(legacyDirectory, "regions.json"));
        _layouts = new CurrencyLayoutStore(Path.Combine(legacyDirectory, "currency-layouts.json"));
        _liveFramePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DesktopClock",
            "cache",
            "currency-live.png");
    }

    public event EventHandler? CurrencyChanged;

    public event EventHandler<ClockMonitorStatus>? StatusChanged;

    public async Task RunAsync(TimeSpan pollingPeriod, CancellationToken cancellationToken)
    {
        string? lastFingerprint = null;
        var lastVisibilityCheck = DateTimeOffset.MinValue;
        var isCurrencyTabVisible = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var region = _regions.GetAll().FirstOrDefault(item =>
                string.Equals(item.Name, CurrencyRegionName, StringComparison.OrdinalIgnoreCase));
            var layout = region is null ? null : _layouts.Get(region.Name);
            if (region is null || layout is null || layout.Slots.Count == 0)
            {
                StatusChanged?.Invoke(this, ClockMonitorStatus.NeedsSetup);
                await Task.Delay(pollingPeriod, cancellationToken);
                continue;
            }

            var gameWindow = _processLocator.FindGameWindow();
            if (gameWindow is null)
            {
                StatusChanged?.Invoke(this, ClockMonitorStatus.WaitingForGame);
                await Task.Delay(pollingPeriod, cancellationToken);
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_liveFramePath)!);
                await _capture.SaveRegionAsync(
                    gameWindow.Handle,
                    region,
                    _liveFramePath,
                    TimeSpan.FromSeconds(2),
                    cancellationToken);
                var fingerprint = CurrencyFrameFingerprint.Create(_liveFramePath, layout);
                var changed = !string.Equals(fingerprint, lastFingerprint, StringComparison.Ordinal);
                var now = DateTimeOffset.UtcNow;
                if (changed || now - lastVisibilityCheck >= TimeSpan.FromSeconds(1))
                {
                    lastVisibilityCheck = now;
                    lastFingerprint = fingerprint;
                    var slots = CurrencyTabProfile.Apply(CurrencyGridDetector.Detect(_liveFramePath));
                    isCurrencyTabVisible = slots.Count >= layout.Slots.Count;
                    if (!isCurrencyTabVisible)
                    {
                        StatusChanged?.Invoke(this, ClockMonitorStatus.WaitingForCurrencyTab);
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, ClockMonitorStatus.Tracking);
                        if (changed)
                        {
                            CurrencyChanged?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                else
                {
                    StatusChanged?.Invoke(this, isCurrencyTabVisible
                        ? ClockMonitorStatus.Tracking
                        : ClockMonitorStatus.WaitingForCurrencyTab);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                StatusChanged?.Invoke(this, ClockMonitorStatus.Error);
            }

            await Task.Delay(pollingPeriod, cancellationToken);
        }
    }
}
