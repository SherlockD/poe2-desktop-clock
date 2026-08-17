namespace Poe2DeskTracker.Pricing;

public sealed record PoeNinjaPriceSnapshot(
    DateTimeOffset RetrievedAt,
    IReadOnlyDictionary<string, decimal> PricesByNormalizedItemName)
{
    public bool TryGetDivinePrice(string itemName, out decimal price) =>
        PricesByNormalizedItemName.TryGetValue(PoeNinjaPriceClient.NormalizeItemName(itemName), out price);
}
