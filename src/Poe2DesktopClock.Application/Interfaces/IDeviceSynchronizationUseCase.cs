using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Доставляет готовый снимок стоимости на устройство отображения.
/// Реализация скрывает используемый транспорт.
/// </summary>
public interface IDeviceSynchronizationUseCase
{
    event EventHandler<DeviceSynchronizationState>? SynchronizationStateChanged;

    DeviceSynchronizationState CurrentState { get; }

    Task<DeviceSynchronizationState> SynchronizeAsync(
        ClockSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
