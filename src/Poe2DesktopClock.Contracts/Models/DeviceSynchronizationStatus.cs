namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Состояние доставки последнего снимка на устройство отображения.
/// Значения не зависят от способа подключения устройства.
/// </summary>
public enum DeviceSynchronizationStatus
{
    WaitingForSnapshot,
    Synchronized,
    Failed,
}
