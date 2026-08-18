namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Представление текущей игровой сессии и динамики оценочной стоимости.
/// </summary>
public sealed record GameSessionSnapshot(
    GameSessionStatus Status,
    DateTimeOffset? StartedAt,
    TimeSpan? Duration,
    ClockSnapshot? BaselineSnapshot,
    ClockSnapshot? CurrentSnapshot,
    decimal? SessionDeltaDivines,
    decimal? DivinesPerHour)
{
    public bool IsGameRunning => Status is not GameSessionStatus.GameNotRunning;

    public bool IsBaselineReady => Status is GameSessionStatus.Tracking;
}
