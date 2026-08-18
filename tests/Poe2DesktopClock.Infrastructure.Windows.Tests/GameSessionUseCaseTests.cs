using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Services;
using Poe2DesktopClock.Contracts.Models;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class GameSessionUseCaseTests
{
    [Fact]
    public void Game_not_running_has_no_session_statistics()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));
        var store = new TestLastClockSnapshotStore(Snapshot(100m));
        var useCase = new GameSessionUseCase(store, time);

        var session = useCase.UpdateGameStatus(GameStatus(false));

        Assert.Equal(GameSessionStatus.GameNotRunning, session.Status);
        Assert.False(session.IsGameRunning);
        Assert.Null(session.StartedAt);
        Assert.Null(session.Duration);
        Assert.Null(session.SessionDeltaDivines);
        Assert.Null(session.DivinesPerHour);
        Assert.Equal(0, store.ReadCalls);
    }

    [Fact]
    public void Game_start_uses_last_persisted_snapshot_as_baseline_and_calculates_divines_per_hour()
    {
        var gameStartedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(gameStartedAt.AddHours(1));
        var baseline = Snapshot(100m);
        var store = new TestLastClockSnapshotStore(baseline);
        var useCase = new GameSessionUseCase(store, time);

        var started = useCase.UpdateGameStatus(GameStatus(true, 123, gameStartedAt));
        var tracked = useCase.UpdateClockSnapshot(Snapshot(130m));

        Assert.Equal(GameSessionStatus.Tracking, started.Status);
        Assert.Same(baseline, started.BaselineSnapshot);
        Assert.Same(baseline, started.CurrentSnapshot);
        Assert.Equal(TimeSpan.FromHours(1), started.Duration);
        Assert.Equal(0m, started.SessionDeltaDivines);
        Assert.Equal(0m, started.DivinesPerHour);

        Assert.Equal(GameSessionStatus.Tracking, tracked.Status);
        Assert.Equal(30m, tracked.SessionDeltaDivines);
        Assert.Equal(30m, tracked.DivinesPerHour);
        Assert.Same(baseline, tracked.BaselineSnapshot);
        Assert.Equal(1, store.ReadCalls);
    }

    [Fact]
    public void First_snapshot_becomes_baseline_when_no_persisted_value_exists_but_duration_starts_with_game()
    {
        var gameStartedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(gameStartedAt);
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(null), time);

        var waiting = useCase.UpdateGameStatus(GameStatus(true, 123), gameStartedAt);
        time.Advance(TimeSpan.FromMinutes(15));
        var baseline = Snapshot(100m);
        var firstValue = useCase.UpdateClockSnapshot(baseline);
        time.Advance(TimeSpan.FromMinutes(30));
        var tracked = useCase.UpdateClockSnapshot(Snapshot(105m));

        Assert.Equal(GameSessionStatus.WaitingForBaseline, waiting.Status);
        Assert.Equal(TimeSpan.Zero, waiting.Duration);
        Assert.Null(waiting.SessionDeltaDivines);
        Assert.Null(waiting.DivinesPerHour);

        Assert.Equal(GameSessionStatus.Tracking, firstValue.Status);
        Assert.Same(baseline, firstValue.BaselineSnapshot);
        Assert.Equal(TimeSpan.FromMinutes(15), firstValue.Duration);
        Assert.Equal(0m, firstValue.SessionDeltaDivines);
        Assert.Equal(0m, firstValue.DivinesPerHour);

        Assert.Equal(TimeSpan.FromMinutes(45), tracked.Duration);
        Assert.Equal(5m, tracked.SessionDeltaDivines);
        Assert.Equal(20m / 3m, tracked.DivinesPerHour);
    }

    [Fact]
    public void Negative_change_is_preserved_in_session_rate()
    {
        var gameStartedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(gameStartedAt.AddMinutes(30));
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(Snapshot(50m)), time);

        useCase.UpdateGameStatus(GameStatus(true, 123), gameStartedAt);
        var session = useCase.UpdateClockSnapshot(Snapshot(45m));

        Assert.Equal(-5m, session.SessionDeltaDivines);
        Assert.Equal(-10m, session.DivinesPerHour);
    }

    [Fact]
    public void Game_stop_clears_session_and_next_process_reads_a_new_baseline()
    {
        var gameStartedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(gameStartedAt.AddMinutes(20));
        var store = new TestLastClockSnapshotStore(Snapshot(100m));
        var useCase = new GameSessionUseCase(store, time);

        useCase.UpdateGameStatus(GameStatus(true, 123), gameStartedAt);
        useCase.UpdateClockSnapshot(Snapshot(110m));
        var stopped = useCase.UpdateGameStatus(GameStatus(false));

        store.LastSnapshot = Snapshot(120m);
        time.Advance(TimeSpan.FromMinutes(10));
        var restarted = useCase.UpdateGameStatus(GameStatus(true, 456), time.GetUtcNow());

        Assert.Equal(GameSessionStatus.GameNotRunning, stopped.Status);
        Assert.Null(stopped.SessionDeltaDivines);
        Assert.Equal(GameSessionStatus.Tracking, restarted.Status);
        Assert.Equal(120m, restarted.BaselineSnapshot?.TotalDivines);
        Assert.Equal(0m, restarted.SessionDeltaDivines);
        Assert.Equal(2, store.ReadCalls);
    }

    [Fact]
    public void Different_process_starts_a_new_session_even_without_an_intermediate_unavailable_status()
    {
        var startedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(startedAt.AddMinutes(30));
        var store = new TestLastClockSnapshotStore(Snapshot(100m));
        var useCase = new GameSessionUseCase(store, time);

        useCase.UpdateGameStatus(GameStatus(true, 123), startedAt);
        useCase.UpdateClockSnapshot(Snapshot(110m));
        store.LastSnapshot = Snapshot(200m);
        var newProcess = useCase.UpdateGameStatus(GameStatus(true, 456), time.GetUtcNow());

        Assert.Equal(GameSessionStatus.Tracking, newProcess.Status);
        Assert.Equal(200m, newProcess.BaselineSnapshot?.TotalDivines);
        Assert.Equal(200m, newProcess.CurrentSnapshot?.TotalDivines);
        Assert.Equal(0m, newProcess.SessionDeltaDivines);
        Assert.Equal(2, store.ReadCalls);
    }

    [Fact]
    public void Fallback_start_time_is_the_first_available_observation()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(Snapshot(100m)), time);

        var started = useCase.UpdateGameStatus(GameStatus(true, 123));
        time.Advance(TimeSpan.FromMinutes(30));
        var tracked = useCase.UpdateClockSnapshot(Snapshot(110m));

        Assert.Equal(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero), started.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(30), tracked.Duration);
        Assert.Equal(20m, tracked.DivinesPerHour);
    }

    private static ClockSnapshot Snapshot(decimal totalDivines) => new(
        totalDivines,
        totalDivines,
        0m,
        DateTimeOffset.UnixEpoch,
        null,
        null,
        true,
        string.Empty);

    private static GameStatus GameStatus(
        bool isAvailable,
        int? processId = null,
        DateTimeOffset? processStartedAt = null) =>
        new(isAvailable, string.Empty, processId, ProcessStartedAt: processStartedAt);

    private sealed class TestLastClockSnapshotStore(ClockSnapshot? lastSnapshot) : ILastClockSnapshotStore
    {
        public ClockSnapshot? LastSnapshot { get; set; } = lastSnapshot;

        public int ReadCalls { get; private set; }

        public ClockSnapshot? GetLastSnapshot()
        {
            ReadCalls++;
            return LastSnapshot;
        }

        public void Save(ClockSnapshot snapshot) => LastSnapshot = snapshot;
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
