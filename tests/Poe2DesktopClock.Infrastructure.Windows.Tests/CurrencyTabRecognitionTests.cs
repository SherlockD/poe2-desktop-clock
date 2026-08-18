using System.Drawing;
using Poe2DesktopClock.Infrastructure.Windows.Monitoring;
using Poe2DeskTracker.Currency;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class CurrencyTabRecognitionTests
{
    [Fact]
    public void Standard_currency_rows_are_recognized_but_an_unrelated_slot_grid_is_not()
    {
        int[] standardRowLengths = [9, 7, 5, 3, 3, 2, 3, 1];
        var standard = CreateRows(standardRowLengths);
        var unrelatedGrid = CreateRows([11, 11, 11]);

        Assert.True(CurrencyTabProfile.MatchesStandardLayout(standard));
        Assert.False(CurrencyTabProfile.MatchesStandardLayout(unrelatedGrid));
    }

    [Fact]
    public void Visibility_requires_the_standard_rows_to_match_saved_calibration_geometry()
    {
        const int imageWidth = 600;
        const int imageHeight = 600;
        var standard = CreateRows([9, 7, 5, 3, 3, 2, 3, 1]);
        var calibration = CreateLayout(standard, imageWidth, imageHeight);
        var shiftedStandardPattern = standard
            .Select(slot => slot with
            {
                Bounds = new Rectangle(
                    slot.Bounds.Left + 100,
                    slot.Bounds.Top,
                    slot.Bounds.Width,
                    slot.Bounds.Height),
            })
            .ToArray();

        Assert.True(CurrencyTabProfile.MatchesCalibratedLayout(
            standard,
            calibration,
            imageWidth,
            imageHeight));
        Assert.False(CurrencyTabProfile.MatchesCalibratedLayout(
            shiftedStandardPattern,
            calibration,
            imageWidth,
            imageHeight));
    }

    [Fact]
    public void First_verified_frame_is_published_immediately_and_reopening_publishes_again()
    {
        var state = new CurrencyFrameObservationState();

        Assert.True(state.ShouldPublish(isCurrencyTabVisible: true, fingerprint: "same"));
        Assert.False(state.ShouldPublish(isCurrencyTabVisible: true, fingerprint: "same"));
        Assert.True(state.ShouldPublish(isCurrencyTabVisible: true, fingerprint: "changed"));
        Assert.False(state.ShouldPublish(isCurrencyTabVisible: false, fingerprint: null));
        Assert.True(state.ShouldPublish(isCurrencyTabVisible: true, fingerprint: "changed"));
    }

    [Theory]
    [InlineData(500, 120, 380)]
    [InlineData(500, 500, 0)]
    [InlineData(500, 720, 0)]
    public void Polling_period_includes_capture_and_analysis_time(
        int periodMilliseconds,
        int elapsedMilliseconds,
        int expectedDelayMilliseconds)
    {
        var delay = WindowsCurrencyChangeMonitor.CalculatePollingDelay(
            TimeSpan.FromMilliseconds(periodMilliseconds),
            TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.Equal(TimeSpan.FromMilliseconds(expectedDelayMilliseconds), delay);
    }

    private static IReadOnlyList<DetectedCurrencySlot> CreateRows(IReadOnlyList<int> rowLengths)
    {
        var slots = new List<DetectedCurrencySlot>();
        for (var rowIndex = 0; rowIndex < rowLengths.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < rowLengths[rowIndex]; columnIndex++)
            {
                slots.Add(new DetectedCurrencySlot(
                    new Rectangle(columnIndex * 50, rowIndex * 70, 40, 40),
                    Confidence: 1));
            }
        }

        return slots;
    }

    private static CurrencyLayout CreateLayout(
        IReadOnlyList<DetectedCurrencySlot> slots,
        int imageWidth,
        int imageHeight) =>
        new(
            "currency",
            imageWidth,
            imageHeight,
            slots.Select((slot, index) => new CurrencySlotDefinition(
                $"slot-{index + 1:D2}",
                (double)slot.Bounds.Left / imageWidth,
                (double)slot.Bounds.Top / imageHeight,
                (double)slot.Bounds.Width / imageWidth,
                (double)slot.Bounds.Height / imageHeight,
                slot.Confidence)).ToList());
}
