namespace Poe2DesktopClock.Application.Models;

/// <summary>Цены в Divine, необходимые одному сценарию оценки.</summary>
public sealed record PriceSnapshot(DateTimeOffset RetrievedAt, IReadOnlyDictionary<string, decimal> DivinePrices)
{
    public bool TryGetDivinePrice(string itemName, out decimal price) =>
        DivinePrices.TryGetValue(itemName, out price);
}
