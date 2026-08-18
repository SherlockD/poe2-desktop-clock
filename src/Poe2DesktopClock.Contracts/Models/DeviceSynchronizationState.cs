namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Подтверждённое устройством состояние синхронизации.
/// </summary>
public sealed record DeviceSynchronizationState(
    bool IsConnected,
    DeviceSynchronizationStatus Status,
    ClockSnapshot? LastSnapshot,
    DateTimeOffset? LastSynchronizedAt)
{
    public bool IsSynchronized => Status == DeviceSynchronizationStatus.Synchronized;

    public static DeviceSynchronizationState WaitingForSnapshot { get; } = new(
        IsConnected: true,
        DeviceSynchronizationStatus.WaitingForSnapshot,
        null,
        null);
}
