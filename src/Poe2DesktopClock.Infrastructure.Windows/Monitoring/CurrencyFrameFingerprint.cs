using System.Security.Cryptography;
using Poe2DeskTracker.Currency;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

/// <summary>
/// Строит короткий отпечаток только областей со счётчиками Currency-вкладки.
/// Поэтому анимация иконок не запускает OCR без изменения количества предметов.
/// </summary>
internal static class CurrencyFrameFingerprint
{
    internal static string Create(string imagePath, CurrencyLayout layout)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var slot in layout.Slots.OrderBy(slot => slot.Y).ThenBy(slot => slot.X))
        {
            var left = Math.Clamp((int)Math.Round((slot.X + slot.Width * 0.04) * image.Width), 0, image.Width - 1);
            var top = Math.Clamp((int)Math.Round((slot.Y + slot.Height * 0.04) * image.Height), 0, image.Height - 1);
            var right = Math.Clamp((int)Math.Round((slot.X + slot.Width * 0.92) * image.Width), left + 1, image.Width);
            var bottom = Math.Clamp((int)Math.Round((slot.Y + slot.Height * 0.42) * image.Height), top + 1, image.Height);

            for (var y = top; y < bottom; y += 2)
            {
                for (var x = left; x < right; x += 2)
                {
                    var pixel = image[x, y];
                    var brightNeutral = pixel.R >= 190 && pixel.G >= 190 && pixel.B >= 180 &&
                                        Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) <= 55;
                    hash.AppendData([brightNeutral ? (byte)1 : (byte)0]);
                }
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
