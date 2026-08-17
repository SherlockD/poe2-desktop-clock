using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.Regions;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

/// <summary>Windows/WGC/OCR implementation of the Currency valuation reader.</summary>
public sealed class WindowsCurrencyValuationReader : ICurrencyValuationReader
{
    private const string CurrencyRegionName = "currency";
    private readonly PoeProcessLocator _processLocator;
    private readonly WindowsGraphicsCaptureService _capture;
    private readonly RegionStore _regions;
    private readonly CurrencyLayoutStore _layouts;
    private readonly string _previewPath;

    public WindowsCurrencyValuationReader(
        PoeProcessLocator processLocator,
        WindowsGraphicsCaptureService capture)
    {
        _processLocator = processLocator;
        _capture = capture;
        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DeskTracker");
        _regions = new RegionStore(Path.Combine(legacyDirectory, "regions.json"));
        _layouts = new CurrencyLayoutStore(Path.Combine(legacyDirectory, "currency-layouts.json"));
        _previewPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DesktopClock",
            "cache",
            "currency-preview.png");
    }

    public async Task<CurrencyValuation?> ReadAsync(PriceSnapshot? prices, CancellationToken cancellationToken = default)
    {
        var region = _regions.GetAll().FirstOrDefault(region =>
            string.Equals(region.Name, CurrencyRegionName, StringComparison.OrdinalIgnoreCase));
        var layout = region is null ? null : _layouts.Get(region.Name);
        var gameWindow = _processLocator.FindGameWindow();
        if (region is null || layout is null || layout.Slots.Count == 0 || gameWindow is null)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_previewPath)!);
        await _capture.SaveRegionAsync(gameWindow.Handle, region, _previewPath, TimeSpan.FromSeconds(5));
        var amounts = await CurrencyAmountScanner.ScanAsync(_previewPath, layout);

        var totalDivines = 0m;
        var unpricedItems = 0;
        var unreadableSlots = 0;
        foreach (var amount in amounts.Where(amount => amount.Amount is null || amount.Amount > 0))
        {
            if (amount.Amount is null)
            {
                unreadableSlots++;
                continue;
            }

            if (prices is null ||
                !CurrencyTabProfile.TryGetPoeNinjaName(amount.Name, out var priceName) ||
                !prices.DivinePrices.TryGetValue(PoeNinjaPriceClient.NormalizeItemName(priceName), out var unitDivines))
            {
                unpricedItems++;
                continue;
            }

            totalDivines += unitDivines * amount.Amount.Value;
        }

        return new CurrencyValuation(totalDivines, unpricedItems, unreadableSlots, DateTimeOffset.UtcNow);
    }
}
