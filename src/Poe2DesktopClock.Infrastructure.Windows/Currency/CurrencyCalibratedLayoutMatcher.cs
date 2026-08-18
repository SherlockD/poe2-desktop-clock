using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Poe2DeskTracker.Currency;

/// <summary>
/// Verifies the already calibrated Currency-tab frames without rediscovering
/// every line in the image. Full grid detection remains part of calibration.
/// </summary>
internal static class CurrencyCalibratedLayoutMatcher
{
    private const double RequiredMatchingSlotRatio = 0.9;
    private const double RequiredGoldRatioPerEdge = 0.55;
    private const int RequiredEdgesPerSlot = 3;

    internal static bool Matches(Image<Rgba32> image, CurrencyLayout layout)
    {
        if (layout.Slots.Count == 0)
        {
            return false;
        }

        var requiredSlots = (int)Math.Ceiling(layout.Slots.Count * RequiredMatchingSlotRatio);
        var matchedSlots = 0;
        for (var index = 0; index < layout.Slots.Count; index++)
        {
            if (MatchesSlot(image, layout.Slots[index]))
            {
                matchedSlots++;
                if (matchedSlots >= requiredSlots)
                {
                    return true;
                }
            }

            var remainingSlots = layout.Slots.Count - index - 1;
            if (matchedSlots + remainingSlots < requiredSlots)
            {
                return false;
            }
        }

        return false;
    }

    private static bool MatchesSlot(Image<Rgba32> image, CurrencySlotDefinition slot)
    {
        var left = Math.Clamp((int)Math.Round(slot.X * image.Width), 0, image.Width - 1);
        var top = Math.Clamp((int)Math.Round(slot.Y * image.Height), 0, image.Height - 1);
        var right = Math.Clamp(
            (int)Math.Round((slot.X + slot.Width) * image.Width) - 1,
            left,
            image.Width - 1);
        var bottom = Math.Clamp(
            (int)Math.Round((slot.Y + slot.Height) * image.Height) - 1,
            top,
            image.Height - 1);
        var width = right - left + 1;
        var height = bottom - top + 1;
        if (width < 8 || height < 8)
        {
            return false;
        }

        var matchingEdges = 0;
        matchingEdges += HasGoldEdge(image, left, top, height, vertical: true) ? 1 : 0;
        matchingEdges += HasGoldEdge(image, right, top, height, vertical: true) ? 1 : 0;
        matchingEdges += HasGoldEdge(image, top, left, width, vertical: false) ? 1 : 0;
        matchingEdges += HasGoldEdge(image, bottom, left, width, vertical: false) ? 1 : 0;
        return matchingEdges >= RequiredEdgesPerSlot;
    }

    private static bool HasGoldEdge(
        Image<Rgba32> image,
        int position,
        int start,
        int length,
        bool vertical)
    {
        var matchingSamples = 0;
        var sampleCount = 0;
        for (var offset = 3; offset < length - 2; offset += 2)
        {
            sampleCount++;
            for (var perpendicularOffset = -2; perpendicularOffset <= 2; perpendicularOffset++)
            {
                var x = vertical ? position + perpendicularOffset : start + offset;
                var y = vertical ? start + offset : position + perpendicularOffset;
                if (x < 0 || x >= image.Width || y < 0 || y >= image.Height)
                {
                    continue;
                }

                if (CurrencyGridDetector.IsFrameGold(image[x, y]))
                {
                    matchingSamples++;
                    break;
                }
            }
        }

        return sampleCount > 0 &&
               matchingSamples / (double)sampleCount >= RequiredGoldRatioPerEdge;
    }
}
