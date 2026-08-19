using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Services;

/// <summary>
/// Рассчитывает динамику стоимости в рамках текущего запуска Path of Exile 2.
/// Последняя полная сохранённая оценка используется как baseline; если надёжной
/// оценки нет, сценарий ждёт первый полный снимок текущего запуска.
/// </summary>
public sealed class GameSessionUseCase : IGameSessionUseCase
{
    private readonly ILastClockSnapshotStore _lastSnapshotStore;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private bool _isGameRunning;
    private int? _processId;
    private DateTimeOffset? _reportedProcessStartedAt;
    private DateTimeOffset? _sessionStartedAt;
    private ClockSnapshot? _baselineSnapshot;
    private ClockSnapshot? _currentSnapshot;

    public GameSessionUseCase(ILastClockSnapshotStore lastSnapshotStore)
        : this(lastSnapshotStore, TimeProvider.System)
    {
    }

    public GameSessionUseCase(ILastClockSnapshotStore lastSnapshotStore, TimeProvider timeProvider)
    {
        _lastSnapshotStore = lastSnapshotStore ?? throw new ArgumentNullException(nameof(lastSnapshotStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public event EventHandler<GameSessionSnapshot>? SessionChanged;

    public GameSessionSnapshot GetCurrentSession()
    {
        lock (_sync)
        {
            return CreateSnapshot(_timeProvider.GetUtcNow());
        }
    }

    public GameSessionSnapshot UpdateGameStatus(GameStatus gameStatus, DateTimeOffset? processStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(gameStatus);

        GameSessionSnapshot snapshot;
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            var observedProcessStartedAt = processStartedAt ?? gameStatus.ProcessStartedAt;
            if (!gameStatus.IsAvailable)
            {
                StopSession();
            }
            else if (!_isGameRunning || IsDifferentProcess(gameStatus.ProcessId, observedProcessStartedAt))
            {
                StartSession(gameStatus.ProcessId, observedProcessStartedAt, now);
            }
            else
            {
                UpdateKnownProcessIdentity(gameStatus.ProcessId, observedProcessStartedAt);
            }

            snapshot = CreateSnapshot(now);
        }

        SessionChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    public GameSessionSnapshot UpdateClockSnapshot(ClockSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        GameSessionSnapshot sessionSnapshot;
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            if (_isGameRunning && IsReliableBaseline(snapshot))
            {
                if (_baselineSnapshot is null)
                {
                    _baselineSnapshot = snapshot;
                    _sessionStartedAt = now;
                }

                _currentSnapshot = snapshot;
            }

            sessionSnapshot = CreateSnapshot(now);
        }

        SessionChanged?.Invoke(this, sessionSnapshot);
        return sessionSnapshot;
    }

    private bool IsDifferentProcess(int? processId, DateTimeOffset? processStartedAt) =>
        (_processId is { } knownProcessId && processId is { } observedProcessId && knownProcessId != observedProcessId) ||
        (_reportedProcessStartedAt is { } knownStartedAt && processStartedAt is { } observedStartedAt && knownStartedAt != observedStartedAt);

    private void StartSession(int? processId, DateTimeOffset? processStartedAt, DateTimeOffset now)
    {
        _isGameRunning = true;
        _processId = processId;
        _reportedProcessStartedAt = processStartedAt;
        var savedSnapshot = _lastSnapshotStore.GetLastSnapshot();
        if (IsReliableBaseline(savedSnapshot))
        {
            _sessionStartedAt = processStartedAt ?? now;
            _baselineSnapshot = savedSnapshot;
            _currentSnapshot = savedSnapshot;
            return;
        }

        _sessionStartedAt = null;
        _baselineSnapshot = null;
        _currentSnapshot = null;
    }

    private void UpdateKnownProcessIdentity(int? processId, DateTimeOffset? processStartedAt)
    {
        _processId ??= processId;
        if (_reportedProcessStartedAt is null && processStartedAt is not null)
        {
            _reportedProcessStartedAt = processStartedAt;
        }
    }

    private void StopSession()
    {
        _isGameRunning = false;
        _processId = null;
        _reportedProcessStartedAt = null;
        _sessionStartedAt = null;
        _baselineSnapshot = null;
        _currentSnapshot = null;
    }

    private static bool IsReliableBaseline(ClockSnapshot? snapshot) =>
        snapshot is
        {
            IsComplete: true,
            CurrencyUpdatedAt: not null,
            PublicTabsUpdatedAt: not null,
        };

    private GameSessionSnapshot CreateSnapshot(DateTimeOffset now)
    {
        if (!_isGameRunning)
        {
            return new GameSessionSnapshot(
                GameSessionStatus.GameNotRunning,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        if (_baselineSnapshot is null || _currentSnapshot is null)
        {
            return new GameSessionSnapshot(
                GameSessionStatus.WaitingForBaseline,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var startedAt = _sessionStartedAt ?? now;
        var duration = now <= startedAt ? TimeSpan.Zero : now - startedAt;
        var delta = _currentSnapshot.TotalDivines - _baselineSnapshot.TotalDivines;
        var perHour = duration.Ticks == 0
            ? 0m
            : delta * TimeSpan.TicksPerHour / duration.Ticks;

        return new GameSessionSnapshot(
            GameSessionStatus.Tracking,
            startedAt,
            duration,
            _baselineSnapshot,
            _currentSnapshot,
            delta,
            perHour);
    }
}
