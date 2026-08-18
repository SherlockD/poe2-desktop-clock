namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Состояние наблюдаемой игровой сессии. Сессия существует только пока запущен процесс игры.
/// </summary>
public enum GameSessionStatus
{
    GameNotRunning,
    WaitingForBaseline,
    Tracking,
}
