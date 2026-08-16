namespace Poe2DesktopClock.Core.Models;

/// <summary>
/// Наблюдаемое состояние окна Path of Exile 2 без привязки к Win32-деталям.
/// </summary>
public sealed record GameStatus(bool IsAvailable, string RussianSummary, int? ProcessId = null, int? Width = null, int? Height = null);
