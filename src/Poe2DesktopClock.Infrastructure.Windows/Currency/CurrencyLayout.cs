namespace Poe2DeskTracker.Currency;

public sealed record CurrencyLayout(
    string RegionName,
    int ReferenceWidth,
    int ReferenceHeight,
    List<CurrencySlotDefinition> Slots);
