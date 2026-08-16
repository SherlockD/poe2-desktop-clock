using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace Poe2DeskTracker.Currency;

internal sealed record DetectedCurrencySlot(
    Rectangle Bounds,
    double Confidence,
    string? Name = null);

/// <summary>
/// Finds slot frames from their visible gold border geometry. It deliberately never
/// uses the captured region's width or height, so a larger or smaller crop remains valid.
/// </summary>
internal static class CurrencyGridDetector
{
    private const int MinimumLineSpan = 24;
    private const int MaximumVerticalGapInsideLine = 3;
    private const int MaximumHorizontalGapInsideLine = 1;
    // Pixel limits are only guard rails for unusually low/high DPI captures.
    // The actual slot size is inferred from repeated frame dimensions on every run.
    private const int MinimumSlotSize = 28;
    private const int MaximumSlotSize = 320;

    internal static IReadOnlyList<DetectedCurrencySlot> Detect(string imagePath)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
        var verticalLines = FindLines(image, vertical: true);
        var horizontalLines = FindLines(image, vertical: false);
        var candidates = FindFrameCandidates(verticalLines, horizontalLines);
        candidates.AddRange(FindFrameCandidatesFromHorizontalBorders(horizontalLines, verticalLines));
        var repeatedSizeCandidates = KeepDominantSlotSize(candidates);
        var selected = SuppressOverlappingCandidates(repeatedSizeCandidates);
        var snapped = SuppressOverlappingCandidates(SnapCandidatesToSlotBounds(image, selected));
        var currencyCandidates = ExcludeAuxiliarySlots(snapped);
        var rowAlignedCandidates = AlignRows(currencyCandidates);

        if (rowAlignedCandidates.Count == 0)
        {
            return [];
        }

        var bestScore = rowAlignedCandidates.Max(candidate => candidate.Score);
        return rowAlignedCandidates
            .OrderBy(candidate => candidate.Bounds.Top)
            .ThenBy(candidate => candidate.Bounds.Left)
            .Select(candidate => new DetectedCurrencySlot(candidate.Bounds, candidate.Score / bestScore))
            .ToArray();
    }

    private static List<FrameCandidate> AlignRows(IReadOnlyList<FrameCandidate> candidates)
    {
        var aligned = new List<FrameCandidate>(candidates.Count);
        foreach (var row in GroupIntoRows(candidates))
        {
            if (row.Count == 1)
            {
                aligned.Add(row[0]);
                continue;
            }

            var commonTop = row.Select(candidate => candidate.Bounds.Top).Order().ElementAt(row.Count / 2);
            aligned.AddRange(row.Select(candidate => candidate with
            {
                Bounds = new Rectangle(candidate.Bounds.Left, commonTop, candidate.Bounds.Width, candidate.Bounds.Height),
            }));
        }

        return aligned;
    }

    private static List<FrameCandidate> SnapCandidatesToSlotBounds(Image<Rgba32> image, IReadOnlyList<FrameCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var commonWidth = FindCommonDimension(candidates.Select(candidate => candidate.Bounds.Width));
        var commonHeight = FindCommonDimension(candidates.Select(candidate => candidate.Bounds.Height));
        return candidates
            .Select(candidate => new FrameCandidate(
                FindStrongestFrameBounds(image, candidate.Bounds, commonWidth, commonHeight),
                candidate.Score))
            .ToList();
    }

    private static int FindCommonDimension(IEnumerable<int> dimensions)
    {
        var values = dimensions.Order().ToArray();
        var requiredOccurrences = Math.Max(3, (int)Math.Ceiling(values.Length * 0.1));
        var commonValue = values
            .GroupBy(value => value)
            .Where(group => group.Count() >= requiredOccurrences)
            .OrderBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault();

        return commonValue == 0 ? values[values.Length / 2] : commonValue;
    }

    private static Rectangle FindStrongestFrameBounds(Image<Rgba32> image, Rectangle candidate, int width, int height)
    {
        var minimumLeft = Math.Max(1, candidate.Left - 8);
        var maximumLeft = Math.Min(image.Width - width - 1, candidate.Right - width + 8);
        var bestLeft = Math.Clamp(candidate.Left, minimumLeft, maximumLeft);
        var bestVerticalEvidence = double.MinValue;
        for (var left = minimumLeft; left <= maximumLeft; left++)
        {
            var evidence = BorderEvidence(image, left, candidate.Top, candidate.Height, vertical: true) +
                           BorderEvidence(image, left + width - 1, candidate.Top, candidate.Height, vertical: true);
            if (evidence > bestVerticalEvidence)
            {
                bestVerticalEvidence = evidence;
                bestLeft = left;
            }
        }

        // Decorative horizontal lines are brighter than several real slot edges.
        // The initial pair of border segments already supplies the trustworthy Y origin.
        var top = Math.Clamp(candidate.Top, 1, image.Height - height - 1);
        return new Rectangle(bestLeft, top, width, height);
    }

    private static double BorderEvidence(Image<Rgba32> image, int position, int start, int length, bool vertical)
    {
        var evidence = 0.0;
        for (var offset = 3; offset < length - 2; offset += 2)
        {
            var bestWeight = 0.0;
            for (var perpendicularOffset = -1; perpendicularOffset <= 1; perpendicularOffset++)
            {
                var x = vertical ? position + perpendicularOffset : start + offset;
                var y = vertical ? start + offset : position + perpendicularOffset;
                if (x < 0 || x >= image.Width || y < 0 || y >= image.Height)
                {
                    continue;
                }

                var pixel = image[x, y];
                bestWeight = Math.Max(bestWeight, FramePixelWeight(pixel));
            }

            evidence += bestWeight;
        }

        return evidence;
    }

    private static double FramePixelWeight(Rgba32 pixel)
    {
        if (!IsFrameGold(pixel))
        {
            return 0;
        }

        return 1 + Math.Clamp((pixel.R + pixel.G - 2 * pixel.B) / 180.0, 0, 1);
    }

    private static List<FrameCandidate> ExcludeAuxiliarySlots(IReadOnlyList<FrameCandidate> candidates)
    {
        var rows = GroupIntoRows(candidates);
        var excluded = new HashSet<FrameCandidate>();

        for (var upperIndex = 0; upperIndex < rows.Count; upperIndex++)
        {
            var upperRow = rows[upperIndex];
            for (var lowerIndex = upperIndex + 1; lowerIndex < rows.Count; lowerIndex++)
            {
                var lowerRow = rows[lowerIndex];
                if (!FormsStorageGrid(upperRow, lowerRow))
                {
                    continue;
                }

                excluded.UnionWith(upperRow);
                excluded.UnionWith(lowerRow);
            }
        }

        foreach (var row in rows.Where(FormsAuxiliaryStrip))
        {
            excluded.UnionWith(row);
        }

        return candidates.Where(candidate => !excluded.Contains(candidate)).ToList();
    }

    private static List<List<FrameCandidate>> GroupIntoRows(IReadOnlyList<FrameCandidate> candidates)
    {
        var rows = new List<List<FrameCandidate>>();
        foreach (var candidate in candidates.OrderBy(candidate => CenterOf(candidate.Bounds).Y))
        {
            var centerY = CenterOf(candidate.Bounds).Y;
            var matchingRow = rows.LastOrDefault(row =>
                Math.Abs(row.Average(existing => CenterOf(existing.Bounds).Y) - centerY) <= Math.Max(3, candidate.Bounds.Height * 0.18));
            if (matchingRow is null)
            {
                matchingRow = [];
                rows.Add(matchingRow);
            }

            matchingRow.Add(candidate);
        }

        return rows;
    }

    private static bool FormsStorageGrid(IReadOnlyList<FrameCandidate> upperRow, IReadOnlyList<FrameCandidate> lowerRow)
    {
        if (upperRow.Count < 5 || lowerRow.Count < 5 || upperRow.Count != lowerRow.Count)
        {
            return false;
        }

        var upper = upperRow.OrderBy(candidate => CenterOf(candidate.Bounds).X).ToArray();
        var lower = lowerRow.OrderBy(candidate => CenterOf(candidate.Bounds).X).ToArray();
        var averageHeight = (upper.Average(candidate => candidate.Bounds.Height) + lower.Average(candidate => candidate.Bounds.Height)) / 2;
        var rowDistance = Math.Abs(CenterOf(lower[0].Bounds).Y - CenterOf(upper[0].Bounds).Y);
        if (rowDistance < averageHeight * 0.6 || rowDistance > averageHeight * 1.5 ||
            !HasRegularSpacing(upper) || !HasRegularSpacing(lower))
        {
            return false;
        }

        return upper.Zip(lower).All(pair =>
            Math.Abs(CenterOf(pair.First.Bounds).X - CenterOf(pair.Second.Bounds).X) <= averageHeight * 0.3);
    }

    private static bool HasRegularSpacing(IReadOnlyList<FrameCandidate> row)
    {
        var centers = row.Select(candidate => CenterOf(candidate.Bounds).X).ToArray();
        var distances = centers.Zip(centers.Skip(1), (first, second) => second - first).ToArray();
        var commonDistance = distances.Order().ElementAt(distances.Length / 2);
        return commonDistance > 0 && distances.All(distance => Math.Abs(distance - commonDistance) <= commonDistance * 0.22);
    }

    // The row of four wide-spaced slots above the 2×7 storage grid is not currency.
    private static bool FormsAuxiliaryStrip(IReadOnlyList<FrameCandidate> row)
    {
        if (row.Count != 4 || !HasRegularSpacing(row))
        {
            return false;
        }

        var ordered = row.OrderBy(candidate => CenterOf(candidate.Bounds).X).ToArray();
        var pitch = CenterOf(ordered[1].Bounds).X - CenterOf(ordered[0].Bounds).X;
        var averageWidth = ordered.Average(candidate => candidate.Bounds.Width);
        return pitch >= averageWidth * 1.1;
    }

    private static List<AxisLine> FindLines(Image<Rgba32> image, bool vertical)
    {
        var scanCount = vertical ? image.Width : image.Height;
        var scanLength = vertical ? image.Height : image.Width;
        var rawLines = new List<AxisLine>();

        for (var fixedCoordinate = 1; fixedCoordinate < scanCount - 1; fixedCoordinate++)
        {
            var segmentStart = -1;
            var lastGold = -1;
            var goldCount = 0;

            for (var offset = 1; offset < scanLength - 1; offset++)
            {
                var isGold = vertical
                    ? IsGoldNear(image, fixedCoordinate, offset, horizontalNeighbourhood: true)
                    : IsGoldNear(image, offset, fixedCoordinate, horizontalNeighbourhood: false);

                if (isGold)
                {
                    segmentStart = segmentStart < 0 ? offset : segmentStart;
                    lastGold = offset;
                    goldCount++;
                    continue;
                }

                var maximumGap = vertical ? MaximumVerticalGapInsideLine : MaximumHorizontalGapInsideLine;
                if (segmentStart >= 0 && offset - lastGold > maximumGap)
                {
                    AddLineIfUseful(rawLines, fixedCoordinate, segmentStart, lastGold, goldCount);
                    segmentStart = -1;
                    lastGold = -1;
                    goldCount = 0;
                }
            }

            if (segmentStart >= 0)
            {
                AddLineIfUseful(rawLines, fixedCoordinate, segmentStart, lastGold, goldCount);
            }
        }

        return MergeParallelLines(rawLines);
    }

    private static void AddLineIfUseful(List<AxisLine> lines, int position, int start, int end, int goldCount)
    {
        var span = end - start + 1;
        if (span >= MinimumLineSpan && goldCount >= span / 4)
        {
            lines.Add(new AxisLine(position, start, end, goldCount));
        }
    }

    private static List<AxisLine> MergeParallelLines(List<AxisLine> rawLines)
    {
        var merged = new List<AxisLine>();
        foreach (var line in rawLines
                     .OrderBy(line => line.Position)
                     .ThenBy(line => line.Start)
                     .ThenByDescending(line => line.GoldPixels))
        {
            var matchingIndex = merged.FindIndex(existing =>
                Math.Abs(existing.Position - line.Position) <= 2 &&
                OverlapRatio(existing, line) >= 0.62);

            if (matchingIndex < 0)
            {
                merged.Add(line);
                continue;
            }

            var existing = merged[matchingIndex];
            var totalWeight = existing.GoldPixels + line.GoldPixels;
            merged[matchingIndex] = new AxisLine(
                Position: (int)Math.Round((existing.Position * (double)existing.GoldPixels + line.Position * line.GoldPixels) / totalWeight),
                Start: Math.Min(existing.Start, line.Start),
                End: Math.Max(existing.End, line.End),
                GoldPixels: totalWeight);
        }

        return merged;
    }

    private static List<FrameCandidate> FindFrameCandidates(IReadOnlyList<AxisLine> verticalLines, IReadOnlyList<AxisLine> horizontalLines)
    {
        var candidates = new List<FrameCandidate>();

        for (var leftIndex = 0; leftIndex < verticalLines.Count; leftIndex++)
        {
            var left = verticalLines[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < verticalLines.Count; rightIndex++)
            {
                var right = verticalLines[rightIndex];
                var width = right.Position - left.Position;
                if (width < MinimumSlotSize || width > MaximumSlotSize)
                {
                    if (width > MaximumSlotSize)
                    {
                        break;
                    }

                    continue;
                }

                var top = Math.Max(left.Start, right.Start);
                var bottom = Math.Min(left.End, right.End);
                var height = bottom - top;
                if (height < MinimumSlotSize || height > MaximumSlotSize || !HasReasonableAspectRatio(width, height))
                {
                    continue;
                }

                var topSupport = FindSupportingLine(horizontalLines, top, left.Position, right.Position);
                var bottomSupport = FindSupportingLine(horizontalLines, bottom, left.Position, right.Position);
                if (topSupport is null || bottomSupport is null)
                {
                    continue;
                }

                var bounds = new Rectangle(left.Position, top, width + 1, height + 1);
                var verticalDensity = (left.GoldPixels / (double)left.Span + right.GoldPixels / (double)right.Span) / 2;
                var horizontalDensity = (topSupport.Value.GoldPixels / (double)topSupport.Value.Span + bottomSupport.Value.GoldPixels / (double)bottomSupport.Value.Span) / 2;
                var endpointAgreement = 1.0 - Math.Min(12, Math.Abs(left.Start - right.Start) + Math.Abs(left.End - right.End)) / 12.0;
                var score = Math.Max(0.1, (verticalDensity + horizontalDensity) * 0.5 + endpointAgreement * 0.35);
                candidates.Add(new FrameCandidate(bounds, score));
            }
        }

        return candidates;
    }

    // Some item art hides a vertical border more than a horizontal one (and vice versa).
    // Looking from the second axis recovers those frames while using the same geometry rules.
    private static List<FrameCandidate> FindFrameCandidatesFromHorizontalBorders(IReadOnlyList<AxisLine> horizontalLines, IReadOnlyList<AxisLine> verticalLines)
    {
        var candidates = new List<FrameCandidate>();

        for (var topIndex = 0; topIndex < horizontalLines.Count; topIndex++)
        {
            var top = horizontalLines[topIndex];
            for (var bottomIndex = topIndex + 1; bottomIndex < horizontalLines.Count; bottomIndex++)
            {
                var bottom = horizontalLines[bottomIndex];
                var height = bottom.Position - top.Position;
                if (height < MinimumSlotSize || height > MaximumSlotSize)
                {
                    if (height > MaximumSlotSize)
                    {
                        break;
                    }

                    continue;
                }

                var left = Math.Max(top.Start, bottom.Start);
                var right = Math.Min(top.End, bottom.End);
                var width = right - left;
                if (width < MinimumSlotSize || width > MaximumSlotSize || !HasReasonableAspectRatio(width, height))
                {
                    continue;
                }

                var leftSupport = FindSupportingLine(verticalLines, left, top.Position, bottom.Position);
                var rightSupport = FindSupportingLine(verticalLines, right, top.Position, bottom.Position);
                if (leftSupport is null || rightSupport is null)
                {
                    continue;
                }

                var bounds = new Rectangle(left, top.Position, width + 1, height + 1);
                var horizontalDensity = (top.GoldPixels / (double)top.Span + bottom.GoldPixels / (double)bottom.Span) / 2;
                var verticalDensity = (leftSupport.Value.GoldPixels / (double)leftSupport.Value.Span + rightSupport.Value.GoldPixels / (double)rightSupport.Value.Span) / 2;
                var endpointAgreement = 1.0 - Math.Min(12, Math.Abs(top.Start - bottom.Start) + Math.Abs(top.End - bottom.End)) / 12.0;
                var score = Math.Max(0.1, (verticalDensity + horizontalDensity) * 0.5 + endpointAgreement * 0.35);
                candidates.Add(new FrameCandidate(bounds, score));
            }
        }

        return candidates;
    }

    private static AxisLine? FindSupportingLine(IReadOnlyList<AxisLine> lines, int expectedPosition, int start, int end)
    {
        AxisLine? best = null;
        var requestedSpan = end - start + 1;
        foreach (var line in lines)
        {
            if (Math.Abs(line.Position - expectedPosition) > 5)
            {
                continue;
            }

            var overlap = Math.Max(0, Math.Min(end, line.End) - Math.Max(start, line.Start) + 1);
            if (overlap < requestedSpan * 0.55)
            {
                continue;
            }

            if (best is null || line.GoldPixels > best.Value.GoldPixels)
            {
                best = line;
            }
        }

        return best;
    }

    private static List<FrameCandidate> KeepDominantSlotSize(List<FrameCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var groups = candidates
            .GroupBy(candidate => (Width: RoundToBucket(candidate.Bounds.Width), Height: RoundToBucket(candidate.Bounds.Height)))
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Average(candidate => candidate.Score))
            .ToArray();
        var dominant = groups[0].Key;

        return candidates
            .Where(candidate =>
                Math.Abs(candidate.Bounds.Width - dominant.Width) <= Math.Max(8, dominant.Width * 0.18) &&
                Math.Abs(candidate.Bounds.Height - dominant.Height) <= Math.Max(8, dominant.Height * 0.18))
            .ToList();
    }

    private static List<FrameCandidate> SuppressOverlappingCandidates(List<FrameCandidate> candidates)
    {
        var selected = new List<FrameCandidate>();
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score))
        {
            var candidateCenter = CenterOf(candidate.Bounds);
            if (selected.Any(existing =>
            {
                var existingCenter = CenterOf(existing.Bounds);
                var minimumWidth = Math.Min(candidate.Bounds.Width, existing.Bounds.Width);
                var minimumHeight = Math.Min(candidate.Bounds.Height, existing.Bounds.Height);
                return Math.Abs(candidateCenter.X - existingCenter.X) < minimumWidth * 0.55 &&
                       Math.Abs(candidateCenter.Y - existingCenter.Y) < minimumHeight * 0.55;
            }))
            {
                continue;
            }

            selected.Add(candidate);
        }

        return selected;
    }

    private static bool IsGoldNear(Image<Rgba32> image, int x, int y, bool horizontalNeighbourhood)
    {
        for (var offset = -1; offset <= 1; offset++)
        {
            var sample = horizontalNeighbourhood ? image[x + offset, y] : image[x, y + offset];
            if (IsFrameGold(sample))
            {
                return true;
            }
        }

        return false;
    }

    // The outer part of the PoE frame shifts from bright gold to a very dark bronze,
    // especially behind some currency artwork. Keep the colour test broad, then rely
    // on four-sided geometry and repeated dimensions to reject ordinary UI texture.
    private static bool IsFrameGold(Rgba32 pixel) =>
        pixel.R >= 42 &&
        pixel.G >= 27 &&
        pixel.R >= pixel.B * 1.2 &&
        pixel.G >= pixel.B * 0.7;

    private static bool HasReasonableAspectRatio(int width, int height)
    {
        var ratio = width / (double)height;
        return ratio is >= 0.64 and <= 1.56;
    }

    private static double OverlapRatio(AxisLine first, AxisLine second)
    {
        var overlap = Math.Max(0, Math.Min(first.End, second.End) - Math.Max(first.Start, second.Start) + 1);
        return overlap / (double)Math.Min(first.Span, second.Span);
    }

    private static int RoundToBucket(int value) => (int)Math.Round(value / 4.0) * 4;

    private static Point CenterOf(Rectangle bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

    private readonly record struct AxisLine(int Position, int Start, int End, int GoldPixels)
    {
        internal int Span => End - Start + 1;
    }

    private sealed record FrameCandidate(Rectangle Bounds, double Score);
}
