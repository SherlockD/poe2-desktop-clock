namespace Poe2DesktopClock.Application.Models;

/// <summary>Оценка последнего успешного считывания Currency-вкладки.</summary>
public sealed record CurrencyValuation(
    decimal Divines,
    int UnpricedItems,
    int UnreadableSlots,
    DateTimeOffset UpdatedAt);
