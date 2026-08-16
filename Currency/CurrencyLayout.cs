namespace Poe2DeskTracker.Currency;

public sealed record CurrencyLayout(
    string RegionName,
    int ReferenceWidth,
    int ReferenceHeight,
    List<CurrencySlotDefinition> Slots);

public sealed record CurrencySlotDefinition(
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    double Confidence,
    string? Name = null);
