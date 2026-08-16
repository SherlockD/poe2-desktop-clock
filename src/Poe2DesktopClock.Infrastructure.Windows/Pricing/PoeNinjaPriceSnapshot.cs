namespace Poe2DeskTracker.Pricing;

internal sealed record PoeNinjaPriceSnapshot(
    DateTimeOffset RetrievedAt,
    IReadOnlyDictionary<string, decimal> PricesByNormalizedItemName)
{
    internal bool TryGetDivinePrice(string itemName, out decimal price) =>
        PricesByNormalizedItemName.TryGetValue(PoeNinjaPriceClient.NormalizeItemName(itemName), out price);
}
