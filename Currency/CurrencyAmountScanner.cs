using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingRectangle = System.Drawing.Rectangle;
using Image = SixLabors.ImageSharp.Image;
using ImageRectangle = SixLabors.ImageSharp.Rectangle;

namespace Poe2DeskTracker.Currency;

internal sealed record CurrencyAmountScanResult(
    string Id,
    string Name,
    long? Amount,
    string RecognizedText,
    DrawingRectangle CountBounds);

/// <summary>
/// Reads only the quantity label anchored in the upper-left part of a saved
/// currency frame. The right edge is deliberately wide enough for any number
/// of digits; the parser itself uses <see cref="long"/> rather than a fixed width.
/// </summary>
internal static class CurrencyAmountScanner
{
    private const int OcrScale = 4;
    private static readonly OcrEngine Ocr = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? throw new InvalidOperationException("The Windows OCR language pack for English (United States) is not available.");

    internal static async Task<IReadOnlyList<CurrencyAmountScanResult>> ScanAsync(string imagePath, CurrencyLayout layout)
    {
        using var image = Image.Load<Rgba32>(imagePath);
        var preparedSlots = new List<PreparedSlotAmount>(layout.Slots.Count);

        foreach (var slot in layout.Slots.OrderBy(slot => slot.Y).ThenBy(slot => slot.X))
        {
            var countBounds = GetCountBounds(slot, image.Width, image.Height);
            using var countImage = image.Clone(context => context.Crop(new ImageRectangle(
                countBounds.Left,
                countBounds.Top,
                countBounds.Width,
                countBounds.Height)));
            using var strictImage = PrepareForOcr(countImage, brightnessThreshold: 190, maximumChannelSpread: 55);
            var hasVisibleAmount = HasInk(strictImage);
            var recognizedText = hasVisibleAmount
                ? await RecognizeAmountAsync(strictImage)
                : string.Empty;
            preparedSlots.Add(new PreparedSlotAmount(
                slot.Id,
                slot.Name ?? slot.Id,
                hasVisibleAmount,
                hasVisibleAmount ? ParseAmount(recognizedText) : 0,
                recognizedText,
                countBounds,
                hasVisibleAmount ? ExtractGlyphMasks(strictImage) : []));
        }

        var digitTemplates = BuildDigitTemplates(preparedSlots);
        var results = new List<CurrencyAmountScanResult>(preparedSlots.Count);
        foreach (var slot in preparedSlots)
        {
            var amount = slot.Amount;
            var recognizedText = slot.RecognizedText;
            if (slot.HasVisibleAmount && !amount.HasValue && TryRecognizeWithDigitTemplates(slot.Glyphs, digitTemplates, out var templateAmount))
            {
                amount = templateAmount;
                recognizedText = templateAmount.ToString(CultureInfo.InvariantCulture);
            }

            results.Add(new CurrencyAmountScanResult(
                slot.Id,
                slot.Name,
                amount,
                recognizedText,
                slot.CountBounds));
        }

        return results;
    }

    private static async Task<string> RecognizeAmountAsync(Image<Rgba32> strictImage)
    {
        using var tightlyCropped = CropToInkWithPadding(strictImage, padding: 4);
        tightlyCropped.Mutate(context => context.Resize(
            tightlyCropped.Width * OcrScale,
            tightlyCropped.Height * OcrScale,
            KnownResamplers.Bicubic));

        var primaryText = await RecognizeAsync(tightlyCropped);
        if (ParseAmount(primaryText).HasValue)
        {
            return primaryText;
        }

        // A single digit, or a digit placed next to a bright part of the item
        // artwork, can be too small for Windows OCR after a tight crop. Retry on
        // the complete count zone: it retains a stable white margin while the
        // strict mask has already removed the coloured artwork.
        using var fullCountZone = strictImage.Clone();
        fullCountZone.Mutate(context => context.Resize(
            fullCountZone.Width * OcrScale,
            fullCountZone.Height * OcrScale,
            KnownResamplers.Bicubic));

        var fallbackText = await RecognizeAsync(fullCountZone);
        if (ParseAmount(fallbackText).HasValue)
        {
            return fallbackText;
        }

        // The game's small single-digit labels have sharp corners which can be
        // softened away by bicubic scaling. A larger nearest-neighbour retry
        // preserves that pixel geometry for the OCR engine.
        using var sharpCropped = CropToInkWithPadding(strictImage, padding: 8);
        sharpCropped.Mutate(context => context.Resize(
            sharpCropped.Width * 6,
            sharpCropped.Height * 6,
            KnownResamplers.NearestNeighbor));

        var sharpText = await RecognizeAsync(sharpCropped);
        return ParseAmount(sharpText).HasValue ? sharpText : primaryText;
    }

    internal static void SaveDebugPreview(string sourceImagePath, string outputPath, IReadOnlyList<CurrencyAmountScanResult> results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var bitmap = new DrawingBitmap(sourceImagePath);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        using var font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Bold);
        using var successPen = new Pen(System.Drawing.Color.Lime, 1.5F);
        using var failurePen = new Pen(System.Drawing.Color.OrangeRed, 1.5F);

        foreach (var result in results)
        {
            var pen = result.Amount.HasValue ? successPen : failurePen;
            graphics.DrawRectangle(pen, result.CountBounds);
            var label = result.Amount?.ToString(CultureInfo.InvariantCulture) ?? "?";
            var labelBounds = new DrawingRectangle(
                result.CountBounds.Left,
                result.CountBounds.Top,
                Math.Max(18, label.Length * 8),
                16);
            using var background = new SolidBrush(System.Drawing.Color.FromArgb(210, System.Drawing.Color.Black));
            using var labelBrush = new SolidBrush(pen.Color);
            graphics.FillRectangle(background, labelBounds);
            graphics.DrawString(label, font, labelBrush, labelBounds.Location);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    internal static void SaveOcrDebugCrops(string imagePath, CurrencyLayout layout, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = Image.Load<Rgba32>(imagePath);
        foreach (var slot in layout.Slots)
        {
            var countBounds = GetCountBounds(slot, image.Width, image.Height);
            using var countImage = image.Clone(context => context.Crop(new ImageRectangle(
                countBounds.Left,
                countBounds.Top,
                countBounds.Width,
                countBounds.Height)));
            using var preparedCountImage = PrepareForOcr(countImage, brightnessThreshold: 190, maximumChannelSpread: 55);
            preparedCountImage.Mutate(context => context.Resize(
                preparedCountImage.Width * OcrScale,
                preparedCountImage.Height * OcrScale,
                KnownResamplers.Bicubic));
            preparedCountImage.SaveAsPng(Path.Combine(outputDirectory, $"{slot.Id}.png"));
        }
    }

    private static DrawingRectangle GetCountBounds(CurrencySlotDefinition slot, int imageWidth, int imageHeight)
    {
        var slotLeft = (int)Math.Round(slot.X * imageWidth);
        var slotTop = (int)Math.Round(slot.Y * imageHeight);
        var slotWidth = Math.Max(1, (int)Math.Round(slot.Width * imageWidth));
        var slotHeight = Math.Max(1, (int)Math.Round(slot.Height * imageHeight));
        var left = slotLeft + Math.Max(2, (int)Math.Round(slotWidth * 0.04));
        var top = slotTop + Math.Max(2, (int)Math.Round(slotHeight * 0.04));
        var right = Math.Min(imageWidth, slotLeft + Math.Max(left - slotLeft + 1, (int)Math.Round(slotWidth * 0.92)));
        var bottom = Math.Min(imageHeight, slotTop + Math.Max(top - slotTop + 1, (int)Math.Round(slotHeight * 0.42)));

        return DrawingRectangle.FromLTRB(left, top, right, bottom);
    }

    private static Image<Rgba32> PrepareForOcr(Image<Rgba32> source, byte brightnessThreshold, int maximumChannelSpread)
    {
        var image = source.Clone();
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    // Stack-count glyphs are near-neutral bright white. Blue,
                    // gold and green item art is rejected before Windows OCR sees it.
                    var isCountPixel = pixel.R >= brightnessThreshold &&
                                       pixel.G >= brightnessThreshold &&
                                       pixel.B >= brightnessThreshold - 10 &&
                                       Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) <= maximumChannelSpread;
                    // Windows OCR is more reliable on the conventional
                    // black-glyphs-on-white-background representation.
                    row[x] = isCountPixel ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255);
                }
            }
        });

        KeepLeftmostGlyphRun(image);
        return image;
    }

    private static void KeepLeftmostGlyphRun(Image<Rgba32> image)
    {
        var labels = new int[image.Height, image.Width];
        var components = new List<BinaryComponent>();
        var nextLabel = 1;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (labels[y, x] != 0 || image[x, y].R >= 128)
                {
                    continue;
                }

                components.Add(LabelComponent(image, labels, x, y, nextLabel));
                nextLabel++;
            }
        }

        var candidates = components
            .Where(component =>
                component.Height >= Math.Max(4, image.Height * 0.3) &&
                component.Area >= 5)
            .OrderBy(component => component.Left)
            .ThenBy(component => component.Top)
            .ToList();
        if (candidates.Count == 0)
        {
            ClearToWhite(image);
            return;
        }

        var keptLabels = new HashSet<int>();
        var previous = candidates[0];
        keptLabels.Add(previous.Label);
        // The PoE typeface leaves a noticeably larger gap after a narrow "1".
        // The vertical-overlap check below still stops the run before item art.
        var maximumGap = Math.Max(4, image.Height / 2);
        foreach (var candidate in candidates.Skip(1))
        {
            var verticalOverlap = Math.Max(0, Math.Min(previous.Bottom, candidate.Bottom) - Math.Max(previous.Top, candidate.Top) + 1);
            if (candidate.Left - previous.Right > maximumGap)
            {
                break;
            }

            if (verticalOverlap < Math.Min(previous.Height, candidate.Height) * 0.45)
            {
                continue;
            }

            keptLabels.Add(candidate.Label);
            previous = candidate;
        }

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (labels[y, x] == 0 || !keptLabels.Contains(labels[y, x]))
                    {
                        row[x] = new Rgba32(255, 255, 255, 255);
                    }
                }
            }
        });
    }

    private static bool HasInk(Image<Rgba32> image)
    {
        var hasInk = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && !hasInk; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].R < 128)
                    {
                        hasInk = true;
                        break;
                    }
                }
            }
        });

        return hasInk;
    }

    private static IReadOnlyDictionary<char, List<GlyphMask>> BuildDigitTemplates(IEnumerable<PreparedSlotAmount> slots)
    {
        var templates = new Dictionary<char, List<GlyphMask>>();
        foreach (var slot in slots)
        {
            if (!slot.Amount.HasValue || slot.Amount == 0)
            {
                continue;
            }

            var digits = string.Concat(slot.RecognizedText.Where(char.IsAsciiDigit));
            if (digits.Length == 0 || digits.Length != slot.Glyphs.Count)
            {
                continue;
            }

            for (var index = 0; index < digits.Length; index++)
            {
                if (!templates.TryGetValue(digits[index], out var variants))
                {
                    variants = [];
                    templates.Add(digits[index], variants);
                }

                variants.Add(slot.Glyphs[index]);
            }
        }

        return templates;
    }

    private static IReadOnlyList<GlyphMask> ExtractGlyphMasks(Image<Rgba32> image)
    {
        var labels = new int[image.Height, image.Width];
        var components = new List<BinaryComponent>();
        var nextLabel = 1;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (labels[y, x] != 0 || image[x, y].R >= 128)
                {
                    continue;
                }

                components.Add(LabelComponent(image, labels, x, y, nextLabel));
                nextLabel++;
            }
        }

        return components
            .Where(component => component.Height >= Math.Max(4, image.Height * 0.3) && component.Area >= 5)
            .OrderBy(component => component.Left)
            .Select(component => CreateGlyphMask(image, component))
            .ToList();
    }

    private static GlyphMask CreateGlyphMask(Image<Rgba32> image, BinaryComponent component)
    {
        const int targetWidth = 16;
        const int targetHeight = 24;
        var sourcePixels = new bool[component.Width * component.Height];
        for (var y = 0; y < component.Height; y++)
        {
            for (var x = 0; x < component.Width; x++)
            {
                sourcePixels[y * component.Width + x] = image[component.Left + x, component.Top + y].R < 128;
            }
        }

        var pixels = new bool[targetWidth * targetHeight];
        for (var targetY = 0; targetY < targetHeight; targetY++)
        {
            var top = component.Top + targetY * component.Height / targetHeight;
            var bottom = Math.Min(
                component.Bottom,
                component.Top + Math.Max(
                    targetY * component.Height / targetHeight + 1,
                    (targetY + 1) * component.Height / targetHeight) - 1);
            for (var targetX = 0; targetX < targetWidth; targetX++)
            {
                var left = component.Left + targetX * component.Width / targetWidth;
                var right = Math.Min(
                    component.Right,
                    component.Left + Math.Max(
                        targetX * component.Width / targetWidth + 1,
                        (targetX + 1) * component.Width / targetWidth) - 1);
                var hasBlackPixel = false;
                for (var y = top; y <= bottom && !hasBlackPixel; y++)
                {
                    for (var x = left; x <= right; x++)
                    {
                        if (image[x, y].R < 128)
                        {
                            hasBlackPixel = true;
                            break;
                        }
                    }
                }

                pixels[targetY * targetWidth + targetX] = hasBlackPixel;
            }
        }

        return new GlyphMask(
            targetWidth,
            targetHeight,
            pixels,
            CountEnclosedWhiteAreas(component.Width, component.Height, sourcePixels),
            component.Width,
            component.Height,
            component.Area);
    }

    private static bool TryRecognizeWithDigitTemplates(
        IReadOnlyList<GlyphMask> glyphs,
        IReadOnlyDictionary<char, List<GlyphMask>> templates,
        out long amount)
    {
        amount = 0;
        var digits = new List<char>();
        foreach (var glyph in glyphs)
        {
            var (digit, distance) = FindClosestDigit(glyph, templates);
            if ((digit is null || distance > 0.24) && LooksLikeEight(glyph))
            {
                digit = '8';
                distance = 0;
            }

            if (digit is null || distance > 0.24)
            {
                break;
            }

            digits.Add(digit.Value);
        }

        return digits.Count > 0 && long.TryParse(string.Concat(digits), NumberStyles.None, CultureInfo.InvariantCulture, out amount);
    }

    private static bool LooksLikeEight(GlyphMask glyph)
    {
        if (glyph.EnclosedWhiteAreas == 2)
        {
            return true;
        }

        // With the strict white-pixel mask, the two loops of PoE's "8" can
        // have a tiny gap. Its compact, dense shape still separates it from
        // 0/6/9, which already have learned templates from this same frame.
        var aspectRatio = (double)glyph.SourceWidth / glyph.SourceHeight;
        var density = (double)glyph.SourceArea / (glyph.SourceWidth * glyph.SourceHeight);
        return aspectRatio is >= 0.48 and <= 0.56 && density >= 0.43;
    }

    private static (char? Digit, double Distance) FindClosestDigit(
        GlyphMask glyph,
        IReadOnlyDictionary<char, List<GlyphMask>> templates)
    {
        char? closestDigit = null;
        var shortestDistance = double.MaxValue;
        foreach (var (digit, variants) in templates)
        {
            foreach (var variant in variants)
            {
                var differenceCount = 0;
                for (var index = 0; index < glyph.Pixels.Length; index++)
                {
                    if (glyph.Pixels[index] != variant.Pixels[index])
                    {
                        differenceCount++;
                    }
                }

                var distance = (double)differenceCount / glyph.Pixels.Length;
                if (distance < shortestDistance)
                {
                    shortestDistance = distance;
                    closestDigit = digit;
                }
            }
        }

        return (closestDigit, shortestDistance);
    }

    private static int CountEnclosedWhiteAreas(int width, int height, bool[] pixels)
    {
        var visited = new bool[pixels.Length];
        var enclosedAreas = 0;
        for (var startY = 0; startY < height; startY++)
        {
            for (var startX = 0; startX < width; startX++)
            {
                var start = startY * width + startX;
                if (visited[start] || pixels[start])
                {
                    continue;
                }

                var pending = new Queue<(int X, int Y)>();
                pending.Enqueue((startX, startY));
                visited[start] = true;
                var touchesEdge = false;
                var area = 0;
                while (pending.TryDequeue(out var point))
                {
                    area++;
                    touchesEdge |= point.X == 0 || point.Y == 0 || point.X == width - 1 || point.Y == height - 1;
                    foreach (var (x, y) in new[] { (point.X - 1, point.Y), (point.X + 1, point.Y), (point.X, point.Y - 1), (point.X, point.Y + 1) })
                    {
                        if (x < 0 || y < 0 || x >= width || y >= height)
                        {
                            continue;
                        }

                        var index = y * width + x;
                        if (visited[index] || pixels[index])
                        {
                            continue;
                        }

                        visited[index] = true;
                        pending.Enqueue((x, y));
                    }
                }

                if (!touchesEdge && area >= 4)
                {
                    enclosedAreas++;
                }
            }
        }

        return enclosedAreas;
    }

    private static void ClearToWhite(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                accessor.GetRowSpan(y).Fill(new Rgba32(255, 255, 255, 255));
            }
        });
    }

    private static BinaryComponent LabelComponent(Image<Rgba32> image, int[,] labels, int startX, int startY, int label)
    {
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue((startX, startY));
        labels[startY, startX] = label;
        var left = startX;
        var right = startX;
        var top = startY;
        var bottom = startY;
        var area = 0;

        while (pending.TryDequeue(out var position))
        {
            area++;
            left = Math.Min(left, position.X);
            right = Math.Max(right, position.X);
            top = Math.Min(top, position.Y);
            bottom = Math.Max(bottom, position.Y);
            for (var y = Math.Max(0, position.Y - 1); y <= Math.Min(image.Height - 1, position.Y + 1); y++)
            {
                for (var x = Math.Max(0, position.X - 1); x <= Math.Min(image.Width - 1, position.X + 1); x++)
                {
                    if (labels[y, x] != 0 || image[x, y].R >= 128)
                    {
                        continue;
                    }

                    labels[y, x] = label;
                    pending.Enqueue((x, y));
                }
            }
        }

        return new BinaryComponent(label, left, top, right, bottom, area);
    }

    private static Image<Rgba32> CropToInkWithPadding(Image<Rgba32> source, int padding)
    {
        var left = source.Width;
        var right = -1;
        var top = source.Height;
        var bottom = -1;
        source.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].R >= 128)
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        });

        if (right < left || bottom < top)
        {
            return source.Clone();
        }

        var result = new Image<Rgba32>(right - left + 1 + padding * 2, bottom - top + 1 + padding * 2, new Rgba32(255, 255, 255, 255));
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                result[x - left + padding, y - top + padding] = source[x, y];
            }
        }

        return result;
    }

    private static async Task<string> RecognizeAsync(Image<Rgba32> image)
    {
        await using var png = new MemoryStream();
        await image.SaveAsPngAsync(png);
        png.Position = 0;

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png.ToArray());
            await writer.StoreAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        var result = await Ocr.RecognizeAsync(softwareBitmap);
        return result.Text.Trim();
    }

    private static long? ParseAmount(string recognizedText)
    {
        var digits = string.Concat(recognizedText.Where(char.IsAsciiDigit));
        return digits.Length > 0 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }

    private readonly record struct BinaryComponent(int Label, int Left, int Top, int Right, int Bottom, int Area)
    {
        internal int Width => Right - Left + 1;
        internal int Height => Bottom - Top + 1;
    }

    private sealed record PreparedSlotAmount(
        string Id,
        string Name,
        bool HasVisibleAmount,
        long? Amount,
        string RecognizedText,
        DrawingRectangle CountBounds,
        IReadOnlyList<GlyphMask> Glyphs);

    private sealed record GlyphMask(
        int Width,
        int Height,
        bool[] Pixels,
        int EnclosedWhiteAreas,
        int SourceWidth,
        int SourceHeight,
        int SourceArea);
}
