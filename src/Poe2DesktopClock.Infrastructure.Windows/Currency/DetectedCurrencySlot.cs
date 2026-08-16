using Rectangle = System.Drawing.Rectangle;

namespace Poe2DeskTracker.Currency;

internal sealed record DetectedCurrencySlot(
    Rectangle Bounds,
    double Confidence,
    string? Name = null);
