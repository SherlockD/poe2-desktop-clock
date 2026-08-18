using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Отслеживает изменение оценочной стоимости в рамках одного запуска игры.
/// </summary>
public interface IGameSessionUseCase
{
    event EventHandler<GameSessionSnapshot>? SessionChanged;

    /// <summary>Возвращает состояние с продолжительностью, рассчитанной на текущий момент.</summary>
    GameSessionSnapshot GetCurrentSession();

    /// <summary>
    /// Регистрирует наблюдаемое состояние процесса игры.
    /// Время старта берётся из <see cref="GameStatus.ProcessStartedAt"/>; <paramref name="processStartedAt"/>
    /// позволяет явно передать его при необходимости. Если оно неизвестно, сессия начинается в момент
    /// первого наблюдения доступной игры.
    /// </summary>
    GameSessionSnapshot UpdateGameStatus(GameStatus gameStatus, DateTimeOffset? processStartedAt = null);

    /// <summary>Передаёт новый успешно рассчитанный снимок стоимости.</summary>
    GameSessionSnapshot UpdateClockSnapshot(ClockSnapshot snapshot);
}
