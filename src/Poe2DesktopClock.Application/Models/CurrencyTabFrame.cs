namespace Poe2DesktopClock.Application.Models;

/// <summary>
/// Immutable reference to one captured Currency-tab image. The producing
/// adapter owns the underlying PNG buffer and must not mutate it after publish.
/// </summary>
public sealed class CurrencyTabFrame
{
    public CurrencyTabFrame(ReadOnlyMemory<byte> pngBytes, DateTimeOffset capturedAt)
    {
        if (pngBytes.IsEmpty)
        {
            throw new ArgumentException("A Currency-tab frame cannot be empty.", nameof(pngBytes));
        }

        PngBytes = pngBytes;
        CapturedAt = capturedAt;
    }

    public ReadOnlyMemory<byte> PngBytes { get; }

    public DateTimeOffset CapturedAt { get; }
}
