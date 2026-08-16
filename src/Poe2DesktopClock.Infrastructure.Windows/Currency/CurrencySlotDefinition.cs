namespace Poe2DeskTracker.Currency;

public sealed record CurrencySlotDefinition(
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    double Confidence,
    string? Name = null);
