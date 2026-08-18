using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.Models;

namespace Poe2DesktopClock.Desktop.Services;

/// <summary>
/// Адаптирует общий runtime к минимальной модели, нужной dashboard-экрану WPF.
/// </summary>
public sealed class RuntimeTrackerStatusProvider : ITrackerStatusProvider, IAsyncDisposable
{
    private readonly ITrackerRefreshUseCase _refresh;
    private readonly ITrackerMonitoringUseCase _monitoring;
    private ClockSnapshot? _clockSnapshot;
    private ClockMonitorStatus _monitorStatus = ClockMonitorStatus.Stopped;

    public RuntimeTrackerStatusProvider(
        ITrackerRefreshUseCase refresh,
        ITrackerMonitoringUseCase monitoring)
    {
        _refresh = refresh;
        _monitoring = monitoring;
        _refresh.ClockSnapshotChanged += OnClockSnapshotChanged;
        _monitoring.MonitorStatusChanged += OnMonitorStatusChanged;
    }

    public event EventHandler<TrackerStatusSnapshot>? StatusChanged;

    public TrackerStatusSnapshot GetCurrent()
    {
        var snapshot = _clockSnapshot;
        var gameStatus = _monitoring.GetGameStatus();
        return new TrackerStatusSnapshot(
            snapshot?.TotalDivines ?? 0m,
            snapshot?.CurrencyUpdatedAt,
            snapshot?.PublicTabsUpdatedAt,
            snapshot?.PricesUpdatedAt,
            FormatCurrencyStatus(gameStatus),
            snapshot?.RussianSummary ?? "Публичные вкладки ещё не были обновлены.",
            snapshot?.IsComplete ?? false);
    }

    public async Task InitializeAsync()
    {
        await _monitoring.StartCurrencyMonitoringAsync();
        PublishCurrent();
    }

    public ValueTask DisposeAsync()
    {
        _refresh.ClockSnapshotChanged -= OnClockSnapshotChanged;
        _monitoring.MonitorStatusChanged -= OnMonitorStatusChanged;
        return ValueTask.CompletedTask;
    }

    private void OnClockSnapshotChanged(object? sender, ClockSnapshot snapshot)
    {
        _clockSnapshot = snapshot;
        PublishCurrent();
    }

    private void OnMonitorStatusChanged(object? sender, ClockMonitorStatus status)
    {
        _monitorStatus = status;
        PublishCurrent();
    }

    private void PublishCurrent() => StatusChanged?.Invoke(this, GetCurrent());

    private string FormatCurrencyStatus(GameStatus gameStatus) => _monitorStatus switch
    {
        ClockMonitorStatus.Tracking => "Currency-вкладка отслеживается.",
        ClockMonitorStatus.WaitingForCurrencyTab => "Откройте Currency-вкладку для отслеживания.",
        ClockMonitorStatus.WaitingForGame => "Ожидание окна Path of Exile 2.",
        ClockMonitorStatus.NeedsSetup => "Выберите область и откалибруйте Currency-вкладку.",
        ClockMonitorStatus.Error => "Наблюдение остановлено из-за ошибки. Проверьте настройки.",
        _ => gameStatus.RussianSummary,
    };
}
