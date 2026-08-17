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
    private readonly ITrackerSettingsUseCase _settings;
    private ClockSnapshot? _clockSnapshot;
    private ClockMonitorStatus _monitorStatus = ClockMonitorStatus.Stopped;
    private string? _lastError;

    public RuntimeTrackerStatusProvider(
        ITrackerRefreshUseCase refresh,
        ITrackerMonitoringUseCase monitoring,
        ITrackerSettingsUseCase settings)
    {
        _refresh = refresh;
        _monitoring = monitoring;
        _settings = settings;
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
            _lastError ?? FormatCurrencyStatus(gameStatus),
            snapshot?.RussianSummary ?? "Публичные вкладки ещё не были обновлены.",
            snapshot?.IsComplete ?? false);
    }

    public async Task InitializeAsync()
    {
        try
        {
            _clockSnapshot = await _refresh.RefreshAsync(refreshPublicTabs: false);
            if (_settings.GetSettings().IsCurrencyMonitoringEnabled)
            {
                await _monitoring.StartCurrencyMonitoringAsync();
            }
        }
        catch (Exception exception)
        {
            _lastError = $"Не удалось обновить данные: {exception.Message}";
        }

        PublishCurrent();
    }

    public async Task RefreshAsync()
    {
        try
        {
            _lastError = null;
            _clockSnapshot = await _refresh.RefreshAsync(refreshPublicTabs: true);
        }
        catch (Exception exception)
        {
            _lastError = $"Не удалось обновить данные: {exception.Message}";
        }

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
