using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using System.Diagnostics;
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

    public WindowsCurrencyChangeMonitor(
        PoeProcessLocator processLocator,
        WindowsGraphicsCaptureService capture,
        RegionStore regions,
        CurrencyLayoutStore layouts)
    {
        _processLocator = processLocator;
        _capture = capture;
        _regions = regions;
        _layouts = layouts;
        _liveFramePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DesktopClock",
            "cache",
            "currency-live.png");
    }

    public event EventHandler<CurrencyTabChangedEventArgs>? CurrencyChanged;

    public event EventHandler<ClockMonitorStatus>? StatusChanged;

    public async Task RunAsync(TimeSpan pollingPeriod, CancellationToken cancellationToken)
    {
        var observation = new CurrencyFrameObservationState();
        while (!cancellationToken.IsCancellationRequested)
        {
            var pollStartedAt = Stopwatch.GetTimestamp();

            try
            {
                var region = _regions.GetAll().FirstOrDefault(item =>
                    string.Equals(item.Name, CurrencyRegionName, StringComparison.OrdinalIgnoreCase));
                var layout = region is null ? null : _layouts.Get(region.Name);
                if (region is null || layout is null || layout.Slots.Count == 0)
                {
                    StatusChanged?.Invoke(this, ClockMonitorStatus.NeedsSetup);
                }
                else
                {
                    var gameWindow = _processLocator.FindGameWindow();
                    if (gameWindow is null)
                    {
                        StatusChanged?.Invoke(this, ClockMonitorStatus.WaitingForGame);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_liveFramePath)!);
                        await _capture.SaveRegionAsync(
                            gameWindow.Handle,
                            region,
                            _liveFramePath,
                            TimeSpan.FromSeconds(2),
                            cancellationToken);
                        var capturedAt = DateTimeOffset.UtcNow;
                        var detectedGrid = await Task.Run(
                            () => CurrencyGridDetector.DetectWithImageSize(_liveFramePath),
                            cancellationToken);
                        var isCurrencyTabVisible = CurrencyTabProfile.MatchesCalibratedLayout(
                            detectedGrid.Slots,
                            layout,
                            detectedGrid.ImageWidth,
                            detectedGrid.ImageHeight);
                        if (!isCurrencyTabVisible)
                        {
                            observation.ShouldPublish(isCurrencyTabVisible: false, fingerprint: null);
                            StatusChanged?.Invoke(this, ClockMonitorStatus.WaitingForCurrencyTab);
                        }
                        else
                        {
                            var fingerprint = await Task.Run(
                                () => CurrencyFrameFingerprint.Create(_liveFramePath, layout),
                                cancellationToken);
                            StatusChanged?.Invoke(this, ClockMonitorStatus.Tracking);
                            if (observation.ShouldPublish(isCurrencyTabVisible: true, fingerprint))
                            {
                                var pngBytes = await File.ReadAllBytesAsync(_liveFramePath, cancellationToken);
                                CurrencyChanged?.Invoke(
                                    this,
                                    new CurrencyTabChangedEventArgs(new CurrencyTabFrame(pngBytes, capturedAt)));
                            }
                        }
                    }
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

            var delay = CalculatePollingDelay(
                pollingPeriod,
                Stopwatch.GetElapsedTime(pollStartedAt));
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    internal static TimeSpan CalculatePollingDelay(TimeSpan pollingPeriod, TimeSpan elapsed)
    {
        var remaining = pollingPeriod - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
