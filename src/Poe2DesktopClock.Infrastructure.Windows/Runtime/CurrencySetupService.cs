using System.Diagnostics;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Interop;
using Poe2DeskTracker.Regions;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Windows.Monitoring;

namespace Poe2DesktopClock.Infrastructure.Windows.Runtime;

/// <summary>
/// Runs the interactive currency-tab setup workflow and owns its persisted
/// prerequisites. Monitoring and refresh consume those prerequisites but do
/// not manipulate setup UI or stores directly.
/// </summary>
internal sealed class CurrencySetupService
{
    private const string CurrencyRegionName = "currency";
    private readonly PoeProcessLocator _processLocator;
    private readonly WindowsGraphicsCaptureService _captureService;
    private readonly RegionStore _regionStore;
    private readonly CurrencyLayoutStore _currencyLayoutStore;
    private readonly string _previewPath;

    internal CurrencySetupService(
        PoeProcessLocator processLocator,
        WindowsGraphicsCaptureService captureService,
        RegionStore regionStore,
        CurrencyLayoutStore currencyLayoutStore,
        string previewPath)
    {
        _processLocator = processLocator;
        _captureService = captureService;
        _regionStore = regionStore;
        _currencyLayoutStore = currencyLayoutStore;
        _previewPath = previewPath;
    }

    internal CurrencySetupStatus GetStatus()
    {
        var prerequisites = GetPrerequisites();
        if (prerequisites is null)
        {
            var hasRegion = _regionStore.GetAll().Any(region => string.Equals(region.Name, CurrencyRegionName, StringComparison.OrdinalIgnoreCase));
            return hasRegion
                ? new CurrencySetupStatus(true, false, "Область Currency выбрана. Нужно откалибровать ячейки.")
                : new CurrencySetupStatus(false, false, "Выберите область Currency-вкладки в настройках.");
        }

        return new CurrencySetupStatus(true, true, "Currency-вкладка готова к отслеживанию.");
    }

    internal async Task SelectRegionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gameWindow = _processLocator.FindGameWindow(includeMinimized: true)
            ?? throw new InvalidOperationException("Path of Exile 2 не найден. Запустите игру и повторите выбор области.");

        Win32Native.RestoreAndActivateWindow(gameWindow.Handle);
        if (!await WaitForStableClientBoundsAsync(gameWindow.Handle, TimeSpan.FromSeconds(2), cancellationToken))
        {
            throw new InvalidOperationException("Окно игры не готово к выбору области. Сделайте его видимым и повторите попытку.");
        }

        var region = await RegionSelectionOverlay.SelectAsync(gameWindow.Handle, CurrencyRegionName);
        if (region is null)
        {
            throw new OperationCanceledException("Выбор области Currency-вкладки отменён.");
        }

        _regionStore.Clear();
        _currencyLayoutStore.Clear();
        _regionStore.Upsert(region);
    }

    internal async Task CalibrateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prerequisites = GetPrerequisites(requireLayout: false)
            ?? throw new InvalidOperationException("Сначала выберите область Currency-вкладки.");
        var gameWindow = _processLocator.FindGameWindow()
            ?? throw new InvalidOperationException("Path of Exile 2 не найден или свёрнут.");

        Directory.CreateDirectory(Path.GetDirectoryName(_previewPath)!);
        await _captureService.SaveRegionAsync(gameWindow.Handle, prerequisites.Region, _previewPath, TimeSpan.FromSeconds(5));
        var detectedSlots = CurrencyTabProfile.Apply(CurrencyGridDetector.Detect(_previewPath));
        if (detectedSlots.Count == 0)
        {
            throw new InvalidOperationException("Не удалось найти ячейки. Откройте Currency-вкладку и повторите калибровку.");
        }

        var layout = await CurrencyCalibrationForm.CalibrateAsync(
            _previewPath,
            prerequisites.Region.Name,
            detectedSlots,
            _currencyLayoutStore.Get(prerequisites.Region.Name));
        if (layout is null)
        {
            throw new OperationCanceledException("Калибровка Currency-вкладки отменена.");
        }

        _currencyLayoutStore.Upsert(layout);
    }

    internal CurrencyPrerequisites? GetPrerequisites(bool requireLayout = true)
    {
        var region = _regionStore.GetAll()
            .FirstOrDefault(saved => string.Equals(saved.Name, CurrencyRegionName, StringComparison.OrdinalIgnoreCase));
        if (region is null)
        {
            return null;
        }

        var layout = _currencyLayoutStore.Get(region.Name);
        if (layout is null || layout.Slots.Count == 0)
        {
            return requireLayout ? null : new CurrencyPrerequisites(region, new CurrencyLayout(region.Name, 0, 0, []));
        }

        return new CurrencyPrerequisites(region, layout);
    }

    private static async Task<bool> WaitForStableClientBoundsAsync(nint windowHandle, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32Native.IsIconic(windowHandle) &&
                Win32Native.TryGetClientBoundsOnScreen(windowHandle, out var left, out var top, out var width, out var height))
            {
                await Task.Delay(150, cancellationToken);
                if (!Win32Native.IsIconic(windowHandle) &&
                    Win32Native.TryGetClientBoundsOnScreen(windowHandle, out var confirmedLeft, out var confirmedTop, out var confirmedWidth, out var confirmedHeight) &&
                    left == confirmedLeft && top == confirmedTop && width == confirmedWidth && height == confirmedHeight)
                {
                    return true;
                }
            }
            else
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return false;
    }
}
