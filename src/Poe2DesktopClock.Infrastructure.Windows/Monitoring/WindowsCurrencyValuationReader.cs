using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Pricing;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

/// <summary>Windows OCR implementation for an already captured Currency-tab frame.</summary>
public sealed class WindowsCurrencyValuationReader : ICurrencyValuationReader
{
    private const string CurrencyRegionName = "currency";
    private readonly CurrencyLayoutStore _layouts;

    public WindowsCurrencyValuationReader(CurrencyLayoutStore layouts) => _layouts = layouts;

    public async Task<CurrencyValuation?> ReadAsync(
        CurrencyTabFrame frame,
        PriceSnapshot? prices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        var layout = _layouts.Get(CurrencyRegionName);
        if (layout is null || layout.Slots.Count == 0)
        {
            return null;
        }

        // Image preprocessing and Windows OCR are CPU-intensive. Keep both
        // outside the WPF dispatcher even when a caller starts a refresh from
        // a UI command.
        var amounts = await Task.Run(
            () => CurrencyAmountScanner.ScanAsync(frame.PngBytes, layout),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

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

        return new CurrencyValuation(totalDivines, unpricedItems, unreadableSlots, frame.CapturedAt);
    }
}
