using System.Drawing;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Pricing;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Infrastructure.Windows.Monitoring;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class ValuationTests
{
    [Fact]
    public void Currency_calculator_tracks_priced_unpriced_and_unreadable_slots()
    {
        var prices = new PoeNinjaPriceSnapshot(
            DateTimeOffset.UtcNow,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["DIVINE ORB"] = 1m,
            });
        var amounts = new[]
        {
            new CurrencyAmountScanResult("divine", "Божественная сфера", 3, "3", Rectangle.Empty),
            new CurrencyAmountScanResult("unknown", "Неизвестная валюта", 2, "2", Rectangle.Empty),
            new CurrencyAmountScanResult("unreadable", "Сфера хаоса", null, string.Empty, Rectangle.Empty),
        };

        var result = CurrencyValuationCalculator.Calculate(amounts, prices, DateTimeOffset.UtcNow);

        Assert.Equal(3m, result.Divines);
        Assert.Equal(1, result.UnpricedItems);
        Assert.Equal(1, result.UnreadableSlots);
    }

    [Fact]
    public void Snapshot_composer_marks_complete_inputs_as_complete()
    {
        var timestamp = DateTimeOffset.UtcNow;

        var snapshot = new ClockSnapshotComposer().Compose(
            new CurrencyValuation(2m, 0, 0, timestamp),
            new PublicTabsValuation(3m, 0, true, timestamp, "ok"),
            timestamp);

        Assert.Equal(5m, snapshot.TotalDivines);
        Assert.True(snapshot.IsComplete);
        Assert.Equal(timestamp, snapshot.PricesUpdatedAt);
    }

    [Fact]
    public void Snapshot_composer_marks_unpriced_public_items_as_incomplete()
    {
        var timestamp = DateTimeOffset.UtcNow;

        var snapshot = new ClockSnapshotComposer().Compose(
            new CurrencyValuation(2m, 0, 0, timestamp),
            new PublicTabsValuation(3m, 1, true, timestamp, "Один тип предметов остался без цены."),
            timestamp);

        Assert.Equal(5m, snapshot.TotalDivines);
        Assert.False(snapshot.IsComplete);
        Assert.Contains("частичная оценка", snapshot.RussianSummary, StringComparison.OrdinalIgnoreCase);
    }
}
