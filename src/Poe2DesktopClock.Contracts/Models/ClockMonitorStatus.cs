namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Состояние фонового наблюдения за Currency-вкладкой.
/// </summary>
public enum ClockMonitorStatus
{
    Stopped,
    WaitingForGame,
    WaitingForCurrencyTab,
    Tracking,
    NeedsSetup,
    Error,
}
