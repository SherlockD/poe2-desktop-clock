namespace Poe2DesktopClock.Core.Models;

/// <summary>
/// Готовность области и слотов Currency-вкладки к фоновому чтению.
/// </summary>
public sealed record CurrencySetupStatus(bool HasRegion, bool HasCalibratedSlots, string RussianSummary);
