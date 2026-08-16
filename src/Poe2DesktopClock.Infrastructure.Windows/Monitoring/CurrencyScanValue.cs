namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

internal sealed record CurrencyScanValue(decimal Divines, int UnpricedItems, int UnreadableSlots, DateTimeOffset UpdatedAt);
