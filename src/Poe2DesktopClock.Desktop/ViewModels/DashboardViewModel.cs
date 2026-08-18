using System.Globalization;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.Models;
using Poe2DesktopClock.Desktop.Services;

namespace Poe2DesktopClock.Desktop.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ITrackerStatusProvider _statusProvider;
    private readonly object _pendingStatusSync = new();
    private TrackerStatusSnapshot _status;
    private TrackerStatusSnapshot? _pendingStatus;
    private bool _statusUpdateScheduled;

    public DashboardViewModel(ITrackerStatusProvider statusProvider)
    {
        _statusProvider = statusProvider;
        _status = statusProvider.GetCurrent();
        _statusProvider.StatusChanged += OnStatusChanged;
    }

    public decimal? TotalDivines => _status.ClockSnapshot?.TotalDivines;

    public string DisplayTotal => FormatDivines(TotalDivines);

    public bool IsGameRunning => _status.Session.IsGameRunning;

    public string GameStatus => _status.GameStatus.RussianSummary;

    public string SessionStatus => _status.Session.Status switch
    {
        GameSessionStatus.Tracking => "Сессия отслеживается",
        GameSessionStatus.WaitingForBaseline => "Ожидание первого расчёта стоимости",
        _ => "Нет активной игровой сессии",
    };

    public string SessionDuration => FormatDuration(_status.Session.Duration);

    public string SessionBaseline => FormatDivines(_status.Session.BaselineSnapshot?.TotalDivines);

    public string SessionDelta => FormatSignedDivines(_status.Session.SessionDeltaDivines);

    public string DivinesPerHour => FormatSignedDivines(_status.Session.DivinesPerHour);

    public string CurrencyTotal => FormatDivines(_status.ClockSnapshot?.CurrencyTabDivines);

    public string CurrencyStatus => _status.MonitorStatus switch
    {
        ClockMonitorStatus.Tracking => "Currency-вкладка отслеживается",
        ClockMonitorStatus.WaitingForCurrencyTab => "Откройте Currency-вкладку",
        ClockMonitorStatus.WaitingForGame => "Ожидание окна игры",
        ClockMonitorStatus.NeedsSetup => "Нужна настройка области и ячеек",
        ClockMonitorStatus.Error => "Ошибка мониторинга",
        _ => "Мониторинг остановлен",
    };

    public string PublicTabsTotal => FormatDivines(_status.ClockSnapshot?.PublicTabsDivines);

    public string PublicStashStatus => _status.ClockSnapshot?.PublicTabsUpdatedAt is null
        ? "Нет сохранённых данных"
        : _status.ClockSnapshot.IsComplete
            ? "Оценка доступна"
            : "Частичная оценка";

    public bool IsEstimateComplete => _status.ClockSnapshot?.IsComplete ?? false;

    public string EstimateQuality => IsEstimateComplete ? "Полная оценка" : "Частичная оценка";

    public string CurrencyUpdatedAt => FormatTimestamp(_status.ClockSnapshot?.CurrencyUpdatedAt);

    public string PublicStashUpdatedAt => FormatTimestamp(_status.ClockSnapshot?.PublicTabsUpdatedAt);

    public string PricesUpdatedAt => FormatTimestamp(_status.ClockSnapshot?.PricesUpdatedAt);

    public string DeviceStatus => _status.Device switch
    {
        { IsConnected: false } => "Устройство отключено",
        { Status: DeviceSynchronizationStatus.Synchronized } => "Эмулятор подтвердил данные",
        { Status: DeviceSynchronizationStatus.Failed } => "Не удалось передать данные",
        _ => "Эмулятор готов к данным",
    };

    public string DeviceLastSynchronizedAt => FormatTimestamp(_status.Device.LastSynchronizedAt);

    public string DeviceDisplayTotal => FormatDivines(_status.Device.LastSnapshot?.TotalDivines);

    public void Refresh()
    {
        _status = _statusProvider.GetCurrent();
        OnPropertyChanged(nameof(TotalDivines));
        OnPropertyChanged(nameof(DisplayTotal));
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(GameStatus));
        OnPropertyChanged(nameof(SessionStatus));
        OnPropertyChanged(nameof(SessionDuration));
        OnPropertyChanged(nameof(SessionBaseline));
        OnPropertyChanged(nameof(SessionDelta));
        OnPropertyChanged(nameof(DivinesPerHour));
        OnPropertyChanged(nameof(CurrencyTotal));
        OnPropertyChanged(nameof(CurrencyStatus));
        OnPropertyChanged(nameof(PublicTabsTotal));
        OnPropertyChanged(nameof(PublicStashStatus));
        OnPropertyChanged(nameof(IsEstimateComplete));
        OnPropertyChanged(nameof(EstimateQuality));
        OnPropertyChanged(nameof(CurrencyUpdatedAt));
        OnPropertyChanged(nameof(PublicStashUpdatedAt));
        OnPropertyChanged(nameof(PricesUpdatedAt));
        OnPropertyChanged(nameof(DeviceStatus));
        OnPropertyChanged(nameof(DeviceLastSynchronizedAt));
        OnPropertyChanged(nameof(DeviceDisplayTotal));
    }

    private void OnStatusChanged(object? sender, TrackerStatusSnapshot status)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UpdateStatus(status);
            return;
        }

        lock (_pendingStatusSync)
        {
            _pendingStatus = status;
            if (_statusUpdateScheduled)
            {
                return;
            }

            _statusUpdateScheduled = true;
        }

        _ = dispatcher.BeginInvoke(
            new Action(ApplyPendingStatus),
            System.Windows.Threading.DispatcherPriority.Normal);
    }

    private void ApplyPendingStatus()
    {
        TrackerStatusSnapshot? status;
        lock (_pendingStatusSync)
        {
            status = _pendingStatus;
            _pendingStatus = null;
            _statusUpdateScheduled = false;
        }

        if (status is not null)
        {
            UpdateStatus(status);
        }
    }

    private void UpdateStatus(TrackerStatusSnapshot status)
    {
        _status = status;
        Refresh();
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp is null
            ? "ещё не обновлялось"
            : timestamp.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    private static string FormatDivines(decimal? value) =>
        value is null
            ? "—"
            : value.Value.ToString("N2", CultureInfo.InvariantCulture);

    private static string FormatSignedDivines(decimal? value)
    {
        if (value is null)
        {
            return "—";
        }

        var prefix = value.Value > 0m ? "+" : string.Empty;
        return $"{prefix}{value.Value.ToString("N2", CultureInfo.InvariantCulture)}";
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "—";
        }

        var value = duration.Value;
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}
