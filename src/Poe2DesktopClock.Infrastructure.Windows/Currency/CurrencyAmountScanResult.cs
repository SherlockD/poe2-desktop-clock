using DrawingRectangle = System.Drawing.Rectangle;

namespace Poe2DeskTracker.Currency;

internal sealed record CurrencyAmountScanResult(
    string Id,
    string Name,
    long? Amount,
    string RecognizedText,
    DrawingRectangle CountBounds);
