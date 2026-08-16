using Poe2DesktopClock.Core.Interfaces;
using Poe2DesktopClock.Core.Models;
using Poe2DesktopClock.Desktop.Models;

namespace Poe2DesktopClock.Desktop.Services;

/// <summary>
/// Адаптирует общий runtime к минимальной модели, нужной dashboard-экрану WPF.
/// </summary>
public sealed class RuntimeTrackerStatusProvider : ITrackerStatusProvider, IAsyncDisposable
{
    private readonly IClockRuntime _runtime;
    private ClockSnapshot? _clockSnapshot;
    private ClockMonitorStatus _monitorStatus = ClockMonitorStatus.Stopped;
    private string? _lastError;

    public RuntimeTrackerStatusProvider(IClockRuntime runtime)
    {
        _runtime = runtime;
        _runtime.ClockSnapshotChanged += OnClockSnapshotChanged;
        _runtime.MonitorStatusChanged += OnMonitorStatusChanged;
    }

    public event EventHandler<TrackerStatusSnapshot>? StatusChanged;

    public TrackerStatusSnapshot GetCurrent()
    {
        var snapshot = _clockSnapshot;
        var gameStatus = _runtime.GetGameStatus();
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
            _clockSnapshot = await _runtime.RefreshAsync(refreshPublicTabs: false);
            if (_runtime.GetSettings().IsCurrencyMonitoringEnabled)
            {
                await _runtime.StartCurrencyMonitoringAsync();
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
            _clockSnapshot = await _runtime.RefreshAsync(refreshPublicTabs: true);
        }
        catch (Exception exception)
        {
            _lastError = $"Не удалось обновить данные: {exception.Message}";
        }

        PublishCurrent();
    }

    public async ValueTask DisposeAsync()
    {
        _runtime.ClockSnapshotChanged -= OnClockSnapshotChanged;
        _runtime.MonitorStatusChanged -= OnMonitorStatusChanged;
        await _runtime.DisposeAsync();
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
