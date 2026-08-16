namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

internal sealed record PublicScanValue(decimal Divines, int UnpricedItems, bool IsComplete, DateTimeOffset UpdatedAt, string RussianSummary);
