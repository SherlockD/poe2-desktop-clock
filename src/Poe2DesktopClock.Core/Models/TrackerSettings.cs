namespace Poe2DesktopClock.Core.Models;

/// <summary>
/// Пользовательские настройки desktop-приложения. Значения здесь не зависят
/// от WPF, файловой системы или конкретного способа захвата окна игры.
/// </summary>
public sealed record TrackerSettings(
    string AccountName,
    string League,
    int CurrencyScreensPerSecond,
    bool IsCurrencyMonitoringEnabled,
    bool IsAutomaticPublicRefreshEnabled,
    int PublicRefreshIntervalMinutes,
    int PriceRefreshIntervalMinutes,
    bool StartMinimized)
{
    public static TrackerSettings Default { get; } = new(
        string.Empty,
        string.Empty,
        CurrencyScreensPerSecond: 2,
        IsCurrencyMonitoringEnabled: true,
        IsAutomaticPublicRefreshEnabled: false,
        PublicRefreshIntervalMinutes: 15,
        PriceRefreshIntervalMinutes: 5,
        StartMinimized: false);

    public TrackerSettings Normalize() => this with
    {
        AccountName = AccountName?.Trim() ?? string.Empty,
        League = League?.Trim() ?? string.Empty,
        CurrencyScreensPerSecond = CurrencyScreensPerSecond is 2 or 3 ? CurrencyScreensPerSecond : 2,
        PublicRefreshIntervalMinutes = Math.Max(10, PublicRefreshIntervalMinutes),
        PriceRefreshIntervalMinutes = Math.Max(1, PriceRefreshIntervalMinutes),
    };
}
