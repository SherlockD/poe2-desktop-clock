namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Наблюдаемое состояние процесса Path of Exile 2 без привязки к Win32-деталям.
/// IsAvailable означает, что процесс игры запущен; окно при этом может быть свёрнуто.
/// </summary>
public sealed record GameStatus(
    bool IsAvailable,
    string RussianSummary,
    int? ProcessId = null,
    int? Width = null,
    int? Height = null,
    DateTimeOffset? ProcessStartedAt = null);
