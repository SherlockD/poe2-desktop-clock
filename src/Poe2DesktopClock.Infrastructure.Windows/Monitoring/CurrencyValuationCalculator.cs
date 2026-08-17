using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Pricing;
using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

/// <summary>
/// Converts recognized currency amounts into a value using one price snapshot.
/// The calculation is deliberately independent from capture and OCR orchestration.
/// </summary>
internal static class CurrencyValuationCalculator
{
    internal static CurrencyValuation Calculate(
        IReadOnlyList<CurrencyAmountScanResult> amounts,
        PoeNinjaPriceSnapshot? prices,
        DateTimeOffset updatedAt)
    {
        var totalDivines = 0m;
        var unreadableSlots = 0;
        var unpricedItems = 0;
        foreach (var amount in amounts.Where(amount => amount.Amount is null || amount.Amount > 0))
        {
            if (amount.Amount is null)
            {
                unreadableSlots++;
                continue;
            }

            if (prices is null ||
                !CurrencyTabProfile.TryGetPoeNinjaName(amount.Name, out var priceName) ||
                !prices.TryGetDivinePrice(priceName, out var unitDivines))
            {
                unpricedItems++;
                continue;
            }

            totalDivines += unitDivines * amount.Amount.Value;
        }

        return new CurrencyValuation(totalDivines, unpricedItems, unreadableSlots, updatedAt);
    }
}
