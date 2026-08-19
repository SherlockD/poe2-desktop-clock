using System.Collections.ObjectModel;
using System.Globalization;
using Poe2DesktopClock.Desktop.Localization;
using Poe2DesktopClock.Desktop.Models;
using Poe2DesktopClock.Desktop.Services;

namespace Poe2DesktopClock.Desktop.ViewModels;

/// <summary>Shows the latest independent valuation for every public tab.</summary>
public sealed class PublicTabsViewModel : ViewModelBase
{
    private readonly ITrackerStatusProvider _statusProvider;
    private readonly object _pendingStatusSync = new();
    private TrackerStatusSnapshot _status;
    private TrackerStatusSnapshot? _pendingStatus;
    private bool _statusUpdateScheduled;

    public PublicTabsViewModel(ITrackerStatusProvider statusProvider)
    {
        _statusProvider = statusProvider;
        _status = statusProvider.GetCurrent();
        Tabs = [];
        ApplyPublicTabsValuation();
        _statusProvider.StatusChanged += OnStatusChanged;
    }

    public ObservableCollection<PublicTabValuationViewModel> Tabs { get; }

    public string TotalDivines => _status.PublicTabsValuation is null
        ? AppStrings.Get("Common_NotAvailable")
        : _status.PublicTabsValuation.Divines.ToString("N2", CultureInfo.InvariantCulture);

    public string UpdatedAt => _status.PublicTabsValuation is null
        ? AppStrings.Get("Common_NotUpdatedYet")
        : _status.PublicTabsValuation.UpdatedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string Summary => _status.PublicTabsValuation?.Summary ??
        AppStrings.Get("PublicTabs_FirstRefreshHint");

    public bool HasTradeApiLimitWarnings => Tabs.Any(tab => tab.HasTradeApiLimitWarning);

    public bool HasTabs => Tabs.Count > 0;

    public string LimitWarningSummary => HasTradeApiLimitWarnings
        ? AppStrings.Get("PublicTabs_LimitWarning")
        : string.Empty;

    public string EmptyMessage => Tabs.Count == 0
        ? AppStrings.Get("PublicTabs_Empty")
        : string.Empty;

    public void Refresh()
    {
        var status = _statusProvider.GetCurrent();
        if (ReferenceEquals(_status.PublicTabsValuation, status.PublicTabsValuation))
        {
            return;
        }

        _status = status;
        ApplyPublicTabsValuation();
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
        if (ReferenceEquals(_status.PublicTabsValuation, status.PublicTabsValuation))
        {
            _status = status;
            return;
        }

        _status = status;
        ApplyPublicTabsValuation();
    }

    private void ApplyPublicTabsValuation()
    {
        Tabs.Clear();
        if (_status.PublicTabsValuation is { } valuation)
        {
            foreach (var tab in valuation.Tabs)
            {
                Tabs.Add(new PublicTabValuationViewModel(tab));
            }
        }

        OnPropertyChanged(nameof(TotalDivines));
        OnPropertyChanged(nameof(UpdatedAt));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasTradeApiLimitWarnings));
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(LimitWarningSummary));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}
