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
        var store = new TestLastClockSnapshotStore(null);
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
    public void Game_start_ignores_an_incomplete_saved_snapshot()
    {
        var gameStartedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(gameStartedAt.AddHours(1));
        var store = new TestLastClockSnapshotStore(Snapshot(500m, isComplete: false));
        var useCase = new GameSessionUseCase(store, time);

        var waiting = useCase.UpdateGameStatus(GameStatus(true, 123, gameStartedAt));

        Assert.Equal(GameSessionStatus.WaitingForBaseline, waiting.Status);
        Assert.True(waiting.IsGameRunning);
        Assert.Null(waiting.StartedAt);
        Assert.Null(waiting.Duration);
        Assert.Null(waiting.BaselineSnapshot);
        Assert.Null(waiting.SessionDeltaDivines);
        Assert.Null(waiting.DivinesPerHour);
        Assert.Equal(1, store.ReadCalls);
    }

    [Fact]
    public void Game_start_uses_a_reliable_saved_snapshot_immediately()
    {
        var gameStartedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(gameStartedAt.AddHours(1));
        var baseline = Snapshot(100m);
        var store = new TestLastClockSnapshotStore(baseline);
        var useCase = new GameSessionUseCase(store, time);

        var started = useCase.UpdateGameStatus(GameStatus(true, 123, gameStartedAt));
        var tracked = useCase.UpdateClockSnapshot(Snapshot(130m));

        Assert.Equal(GameSessionStatus.Tracking, started.Status);
        Assert.Equal(gameStartedAt, started.StartedAt);
        Assert.Equal(TimeSpan.FromHours(1), started.Duration);
        Assert.Same(baseline, started.BaselineSnapshot);
        Assert.Equal(0m, started.SessionDeltaDivines);
        Assert.Equal(0m, started.DivinesPerHour);
        Assert.Equal(30m, tracked.SessionDeltaDivines);
        Assert.Equal(30m, tracked.DivinesPerHour);
        Assert.Equal(1, store.ReadCalls);
    }

    [Fact]
    public void First_complete_snapshot_starts_the_session_and_becomes_its_baseline()
    {
        var detectedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(detectedAt);
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(null), time);
        useCase.UpdateGameStatus(GameStatus(true, 123, detectedAt.AddHours(-2)));

        time.Advance(TimeSpan.FromMinutes(15));
        var baseline = Snapshot(100m);
        var started = useCase.UpdateClockSnapshot(baseline);
        time.Advance(TimeSpan.FromMinutes(30));
        var tracked = useCase.UpdateClockSnapshot(Snapshot(105m));

        Assert.Equal(GameSessionStatus.Tracking, started.Status);
        Assert.Equal(detectedAt.AddMinutes(15), started.StartedAt);
        Assert.Equal(TimeSpan.Zero, started.Duration);
        Assert.Same(baseline, started.BaselineSnapshot);
        Assert.Equal(0m, started.SessionDeltaDivines);
        Assert.Equal(0m, started.DivinesPerHour);

        Assert.Equal(TimeSpan.FromMinutes(30), tracked.Duration);
        Assert.Equal(5m, tracked.SessionDeltaDivines);
        Assert.Equal(10m, tracked.DivinesPerHour);
    }

    [Fact]
    public void Incomplete_snapshot_does_not_start_the_session()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(null), time);
        useCase.UpdateGameStatus(GameStatus(true, 123));

        var waiting = useCase.UpdateClockSnapshot(Snapshot(500m, isComplete: false));
        time.Advance(TimeSpan.FromMinutes(20));
        var started = useCase.UpdateClockSnapshot(Snapshot(100m));

        Assert.Equal(GameSessionStatus.WaitingForBaseline, waiting.Status);
        Assert.Null(waiting.BaselineSnapshot);
        Assert.Equal(TimeSpan.Zero, started.Duration);
        Assert.Equal(100m, started.BaselineSnapshot?.TotalDivines);
        Assert.Equal(0m, started.DivinesPerHour);
    }

    [Fact]
    public void Incomplete_snapshot_does_not_change_an_active_session_value()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(null), time);
        useCase.UpdateGameStatus(GameStatus(true, 123));
        useCase.UpdateClockSnapshot(Snapshot(100m));
        time.Advance(TimeSpan.FromMinutes(30));
        useCase.UpdateClockSnapshot(Snapshot(110m));

        time.Advance(TimeSpan.FromMinutes(30));
        var afterIncomplete = useCase.UpdateClockSnapshot(Snapshot(25m, isComplete: false));

        Assert.Equal(110m, afterIncomplete.CurrentSnapshot?.TotalDivines);
        Assert.Equal(10m, afterIncomplete.SessionDeltaDivines);
        Assert.Equal(10m, afterIncomplete.DivinesPerHour);
    }

    [Fact]
    public void Negative_change_is_preserved_in_session_rate()
    {
        var startedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(startedAt);
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(null), time);

        useCase.UpdateGameStatus(GameStatus(true, 123), startedAt.AddHours(-1));
        useCase.UpdateClockSnapshot(Snapshot(50m));
        time.Advance(TimeSpan.FromMinutes(30));
        var session = useCase.UpdateClockSnapshot(Snapshot(45m));

        Assert.Equal(-5m, session.SessionDeltaDivines);
        Assert.Equal(-10m, session.DivinesPerHour);
    }

    [Fact]
    public void Game_stop_clears_session_and_next_process_uses_the_latest_reliable_saved_baseline()
    {
        var startedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(startedAt);
        var store = new TestLastClockSnapshotStore(null);
        var useCase = new GameSessionUseCase(store, time);

        useCase.UpdateGameStatus(GameStatus(true, 123), startedAt);
        useCase.UpdateClockSnapshot(Snapshot(100m));
        time.Advance(TimeSpan.FromMinutes(20));
        useCase.UpdateClockSnapshot(Snapshot(110m));
        store.Save(Snapshot(110m));
        var stopped = useCase.UpdateGameStatus(GameStatus(false));
        var restarted = useCase.UpdateGameStatus(GameStatus(true, 456), time.GetUtcNow());

        Assert.Equal(GameSessionStatus.GameNotRunning, stopped.Status);
        Assert.Null(stopped.SessionDeltaDivines);
        Assert.Equal(GameSessionStatus.Tracking, restarted.Status);
        Assert.Equal(110m, restarted.BaselineSnapshot?.TotalDivines);
        Assert.Equal(0m, restarted.SessionDeltaDivines);
    }

    [Fact]
    public void Different_process_starts_a_new_session_even_without_an_unavailable_status()
    {
        var startedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(startedAt);
        var store = new TestLastClockSnapshotStore(null);
        var useCase = new GameSessionUseCase(store, time);

        useCase.UpdateGameStatus(GameStatus(true, 123), startedAt);
        useCase.UpdateClockSnapshot(Snapshot(100m));
        time.Advance(TimeSpan.FromMinutes(30));
        useCase.UpdateClockSnapshot(Snapshot(110m));
        store.Save(Snapshot(200m));
        var newProcess = useCase.UpdateGameStatus(GameStatus(true, 456), time.GetUtcNow());

        Assert.Equal(GameSessionStatus.Tracking, newProcess.Status);
        Assert.Equal(200m, newProcess.BaselineSnapshot?.TotalDivines);
        Assert.Equal(0m, newProcess.SessionDeltaDivines);
    }

    [Fact]
    public void Reported_process_start_time_does_not_inflate_the_rate_before_the_baseline()
    {
        var detectedAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(detectedAt);
        var useCase = new GameSessionUseCase(new TestLastClockSnapshotStore(null), time);

        useCase.UpdateGameStatus(GameStatus(true, 123, detectedAt.AddHours(-3)));
        var baseline = useCase.UpdateClockSnapshot(Snapshot(100m));
        time.Advance(TimeSpan.FromMinutes(30));
        var tracked = useCase.UpdateClockSnapshot(Snapshot(110m));

        Assert.Equal(detectedAt, baseline.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(30), tracked.Duration);
        Assert.Equal(20m, tracked.DivinesPerHour);
    }

    private static ClockSnapshot Snapshot(decimal totalDivines, bool isComplete = true) => new(
        totalDivines,
        totalDivines,
        0m,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        isComplete,
        isComplete ? "Полная оценка." : "Частичная оценка.");

    private static GameStatus GameStatus(
        bool isAvailable,
        int? processId = null,
        DateTimeOffset? processStartedAt = null) =>
        new(isAvailable, string.Empty, processId, ProcessStartedAt: processStartedAt);

    private sealed class TestLastClockSnapshotStore(ClockSnapshot? lastSnapshot) : ILastClockSnapshotStore
    {
        public int ReadCalls { get; private set; }

        public ClockSnapshot? GetLastSnapshot()
        {
            ReadCalls++;
            return lastSnapshot;
        }

        public void Save(ClockSnapshot snapshot) => lastSnapshot = snapshot;
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
