using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Poe2DesktopClock.Desktop.Infrastructure;
using Poe2DesktopClock.Desktop.Models;
using Poe2DesktopClock.Desktop.Services;

namespace Poe2DesktopClock.Desktop.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ITrackerStatusProvider _statusProvider;
    private TrackerStatusSnapshot _status;

    public DashboardViewModel(ITrackerStatusProvider statusProvider)
    {
        _statusProvider = statusProvider;
        _status = statusProvider.GetCurrent();
        _statusProvider.StatusChanged += OnStatusChanged;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public decimal TotalDivines => _status.TotalDivines;

    public string DisplayTotal => TotalDivines.ToString("N2", CultureInfo.InvariantCulture);

    public string CurrencyStatus => _status.CurrencyStatus;

    public string PublicStashStatus => _status.PublicStashStatus;

    public bool IsEstimateComplete => _status.IsEstimateComplete;

    public string EstimateQuality => IsEstimateComplete ? "Полная оценка" : "Частичная оценка";

    public string CurrencyUpdatedAt => FormatTimestamp(_status.CurrencyUpdatedAt);

    public string PublicStashUpdatedAt => FormatTimestamp(_status.PublicStashUpdatedAt);

    public string PricesUpdatedAt => FormatTimestamp(_status.PricesUpdatedAt);

    public ICommand RefreshCommand { get; }

    public void Refresh()
    {
        _status = _statusProvider.GetCurrent();
        OnPropertyChanged(nameof(TotalDivines));
        OnPropertyChanged(nameof(DisplayTotal));
        OnPropertyChanged(nameof(CurrencyStatus));
        OnPropertyChanged(nameof(PublicStashStatus));
        OnPropertyChanged(nameof(IsEstimateComplete));
        OnPropertyChanged(nameof(EstimateQuality));
        OnPropertyChanged(nameof(CurrencyUpdatedAt));
        OnPropertyChanged(nameof(PublicStashUpdatedAt));
        OnPropertyChanged(nameof(PricesUpdatedAt));
    }

    private async Task RefreshAsync()
    {
        await _statusProvider.RefreshAsync();
    }

    private void OnStatusChanged(object? sender, TrackerStatusSnapshot status)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UpdateStatus(status);
            return;
        }

        dispatcher.BeginInvoke(() => UpdateStatus(status));
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
}
