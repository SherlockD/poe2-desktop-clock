using Poe2DeskTracker.Currency;

internal sealed record CurrencyScreenScan(
    IReadOnlyList<CurrencyAmountScanResult> Amounts,
    string DebugPath);
