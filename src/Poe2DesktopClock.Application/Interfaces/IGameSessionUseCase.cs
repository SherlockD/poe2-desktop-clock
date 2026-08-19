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
    /// <see cref="GameStatus.ProcessStartedAt"/> и <paramref name="processStartedAt"/> используются
    /// для распознавания смены процесса и времени старта статистики при наличии сохранённого baseline.
    /// Если надёжного сохранённого снимка нет, сценарий ждёт первый полный снимок текущего запуска.
    /// </summary>
    GameSessionSnapshot UpdateGameStatus(GameStatus gameStatus, DateTimeOffset? processStartedAt = null);

    /// <summary>
    /// Передаёт новый снимок стоимости. Неполный снимок не запускает сессию и не меняет её статистику.
    /// </summary>
    GameSessionSnapshot UpdateClockSnapshot(ClockSnapshot snapshot);
}
