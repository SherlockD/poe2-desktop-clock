using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.Infrastructure;
using Poe2DesktopClock.Desktop.Localization;

namespace Poe2DesktopClock.Desktop.ViewModels;

/// <summary>
/// Editable desktop settings and the user-facing setup actions for Currency
/// and public stash tabs.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ITrackerSettingsUseCase _settings;
    private readonly ILeagueCatalog _leagueCatalog;
    private readonly ICurrencySetupUseCase _currencySetup;
    private readonly ITrackerMonitoringUseCase _monitoring;
    private readonly IPublicTabsSetupUseCase _publicTabsSetup;
    private readonly AsyncRelayCommand _synchronizePublicTabsCommand;
    private readonly AsyncRelayCommand _savePublicTabsCommand;
    private readonly RelayCommand _cancelPublicTabsSynchronizationCommand;
    private readonly HashSet<string> _configuredPublicTabLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _verifiedPublicTabLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PublicTabSynchronizationResult> _latestPublicTabResults = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _publicTabsSynchronizationCancellation;
    private string _configuredAccountName = string.Empty;
    private string _configuredLeague = string.Empty;
    private string _accountName = string.Empty;
    private string _selectedLeague = string.Empty;
    private bool _isCurrencyTrackingEnabled = true;
    private int _selectedCaptureFps = 2;
    private bool _startMinimized;
    private bool _isCaptureBorderEnabled = true;
    private bool _isSynchronizingPublicTabs;
    private string _notice = AppStrings.Get("Settings_InitialNotice");

    public SettingsViewModel(
        ITrackerSettingsUseCase settings,
        ILeagueCatalog leagueCatalog,
        ICurrencySetupUseCase currencySetup,
        ITrackerMonitoringUseCase monitoring,
        IPublicTabsSetupUseCase publicTabsSetup)
    {
        _settings = settings;
        _leagueCatalog = leagueCatalog;
        _currencySetup = currencySetup;
        _monitoring = monitoring;
        _publicTabsSetup = publicTabsSetup;
        CaptureFrequencies = new ObservableCollection<int>([2, 3]);
        Leagues = [];
        PublicTabs = [];
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RefreshLeaguesCommand = new AsyncRelayCommand(RefreshLeaguesAsync);
        SelectCurrencyAreaCommand = new AsyncRelayCommand(SelectCurrencyAreaAsync);
        CalibrateCurrencyCommand = new AsyncRelayCommand(CalibrateCurrencyAsync);
        _synchronizePublicTabsCommand = new AsyncRelayCommand(
            SynchronizePublicTabsAsync,
            CanSynchronizePublicTabs);
        _savePublicTabsCommand = new AsyncRelayCommand(
            SavePublicTabsAsync,
            CanSavePublicTabs);
        _cancelPublicTabsSynchronizationCommand = new RelayCommand(
            CancelPublicTabsSynchronization,
            () => IsSynchronizingPublicTabs);
    }

    public ObservableCollection<int> CaptureFrequencies { get; }

    public ObservableCollection<string> Leagues { get; }

    public ObservableCollection<PublicTabMarkerViewModel> PublicTabs { get; }

    public string AccountName
    {
        get => _accountName;
        set
        {
            if (SetProperty(ref _accountName, value))
            {
                OnPublicTabsInputsChanged();
            }
        }
    }

    public string SelectedLeague
    {
        get => _selectedLeague;
        set
        {
            if (SetProperty(ref _selectedLeague, value))
            {
                OnPublicTabsInputsChanged();
            }
        }
    }

    public bool IsCurrencyTrackingEnabled
    {
        get => _isCurrencyTrackingEnabled;
        set => SetProperty(ref _isCurrencyTrackingEnabled, value);
    }

    public int SelectedCaptureFps
    {
        get => _selectedCaptureFps;
        set => SetProperty(ref _selectedCaptureFps, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public bool IsCaptureBorderEnabled
    {
        get => _isCaptureBorderEnabled;
        set => SetProperty(ref _isCaptureBorderEnabled, value);
    }

    public string Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    public bool IsSynchronizingPublicTabs
    {
        get => _isSynchronizingPublicTabs;
        private set
        {
            if (!SetProperty(ref _isSynchronizingPublicTabs, value))
            {
                return;
            }

            UpdatePublicTabsCommandAvailability();
        }
    }

    public string PublicTabsSummary => AppStrings.Format(
        "Settings_SelectedPublicTabsFormat",
        PublicTabs.Count(tab => tab.IsIncluded),
        PublicTabs.Count);

    public ICommand SaveCommand { get; }

    public ICommand RefreshLeaguesCommand { get; }

    public ICommand SelectCurrencyAreaCommand { get; }

    public ICommand CalibrateCurrencyCommand { get; }

    public ICommand SynchronizePublicTabsCommand => _synchronizePublicTabsCommand;

    public ICommand SavePublicTabsCommand => _savePublicTabsCommand;

    public ICommand CancelPublicTabsSynchronizationCommand => _cancelPublicTabsSynchronizationCommand;

    public async Task LoadAsync()
    {
        ApplySettings(_settings.GetSettings());
        LoadConfiguredPublicTabs();
        await RefreshLeaguesAsync();
    }

    /// <summary>Stops the in-flight Trade API operation during application shutdown.</summary>
    public void CancelPendingOperations() => _publicTabsSynchronizationCancellation?.Cancel();

    private async Task SaveAsync()
    {
        _settings.SaveSettings(CreateSettings());
        await _monitoring.StopCurrencyMonitoringAsync();
        await _monitoring.StartCurrencyMonitoringAsync();

        Notice = HasPublicTabsChanges
            ? AppStrings.Get("Settings_SavedPublicTabsPending")
            : AppStrings.Get("Settings_Saved");
    }

    private async Task RefreshLeaguesAsync()
    {
        try
        {
            var leagues = await _leagueCatalog.GetPoe2LeaguesAsync();
            Leagues.Clear();
            foreach (var league in leagues)
            {
                Leagues.Add(league);
            }

            if (string.IsNullOrWhiteSpace(SelectedLeague))
            {
                SelectedLeague = leagues.FirstOrDefault(league =>
                                    !league.StartsWith("HC", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(league, "Standard", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(league, "Hardcore", StringComparison.OrdinalIgnoreCase))
                                ?? leagues.FirstOrDefault()
                                ?? string.Empty;
            }

            Notice = AppStrings.Get("Settings_LeaguesUpdated");
        }
        catch (Exception exception)
        {
            Notice = AppStrings.Format("Settings_LeaguesUpdateFailedFormat", exception.Message);
        }
    }

    private async Task SelectCurrencyAreaAsync()
    {
        try
        {
            await _currencySetup.SelectCurrencyRegionAsync();
            Notice = AppStrings.Get("Settings_AreaSelected");
        }
        catch (OperationCanceledException)
        {
            Notice = AppStrings.Get("Settings_AreaSelectionCancelled");
        }
        catch (Exception exception)
        {
            Notice = AppStrings.Format("Settings_AreaSelectionFailedFormat", exception.Message);
        }
    }

    private async Task CalibrateCurrencyAsync()
    {
        try
        {
            await _currencySetup.CalibrateCurrencySlotsAsync();
            Notice = AppStrings.Get("Settings_SlotsSaved");
        }
        catch (OperationCanceledException)
        {
            Notice = AppStrings.Get("Settings_CalibrationCancelled");
        }
        catch (Exception exception)
        {
            Notice = AppStrings.Format("Settings_CalibrationFailedFormat", exception.Message);
        }
    }

    private void ApplySettings(TrackerSettings settings)
    {
        AccountName = settings.AccountName;
        SelectedLeague = settings.League;
        IsCurrencyTrackingEnabled = settings.IsCurrencyMonitoringEnabled;
        SelectedCaptureFps = settings.CurrencyScreensPerSecond;
        StartMinimized = settings.StartMinimized;
        IsCaptureBorderEnabled = settings.IsCaptureBorderEnabled;
    }

    private void LoadConfiguredPublicTabs()
    {
        foreach (var existing in PublicTabs)
        {
            existing.PropertyChanged -= OnPublicTabPropertyChanged;
        }

        PublicTabs.Clear();
        _configuredPublicTabLabels.Clear();
        _verifiedPublicTabLabels.Clear();
        _latestPublicTabResults.Clear();
        var hasSavedConfiguration = _publicTabsSetup.HasSavedConfiguration();
        foreach (var tab in _publicTabsSetup.GetTabs())
        {
            var viewModel = new PublicTabMarkerViewModel(
                tab.Label,
                tab.TabName,
                tab.PriceAmount,
                tab.PriceCurrency,
                tab.IsSelected);
            viewModel.PropertyChanged += OnPublicTabPropertyChanged;
            PublicTabs.Add(viewModel);
            if (hasSavedConfiguration && tab.IsSelected)
            {
                _configuredPublicTabLabels.Add(tab.Label);
                _verifiedPublicTabLabels.Add(tab.Label);
            }
        }

        _configuredAccountName = hasSavedConfiguration ? Normalize(AccountName) : string.Empty;
        _configuredLeague = hasSavedConfiguration ? Normalize(SelectedLeague) : string.Empty;
        RefreshPublicTabPresentation();
    }

    private TrackerSettings CreateSettings() => new(
        AccountName,
        SelectedLeague,
        SelectedCaptureFps,
        IsCurrencyTrackingEnabled,
        IsAutomaticPublicRefreshEnabled: true,
        PublicRefreshIntervalMinutes: 2,
        PriceRefreshIntervalMinutes: 30,
        StartMinimized,
        IsCaptureBorderEnabled);

    private async Task SynchronizePublicTabsAsync()
    {
        if (!CanSynchronizePublicTabs())
        {
            Notice = AppStrings.Get("Settings_PublicTabsInputRequired");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _publicTabsSynchronizationCancellation = cancellation;
        IsSynchronizingPublicTabs = true;
        Notice = AppStrings.Get("Settings_PublicTabsSynchronizationStarted");
        try
        {
            var result = await _publicTabsSetup.SynchronizeAsync(CreatePublicTabsRequest(), cancellation.Token);
            _latestPublicTabResults.Clear();
            _verifiedPublicTabLabels.Clear();
            foreach (var tabResult in result.Tabs)
            {
                _latestPublicTabResults[tabResult.Tab.Label] = tabResult;
                if (tabResult.IsSynchronized)
                {
                    _verifiedPublicTabLabels.Add(tabResult.Tab.Label);
                }
            }

            Notice = result.AreAllSelectedTabsSynchronized
                ? AppStrings.Get("Settings_PublicTabsSynchronizationSucceeded")
                : AppStrings.Get("Settings_PublicTabsSynchronizationFailed");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Notice = AppStrings.Get("Settings_PublicTabsSynchronizationCancelled");
        }
        catch (Exception exception)
        {
            Notice = AppStrings.Format("Settings_PublicTabsSynchronizationErrorFormat", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_publicTabsSynchronizationCancellation, cancellation))
            {
                _publicTabsSynchronizationCancellation = null;
            }

            IsSynchronizingPublicTabs = false;
            RefreshPublicTabPresentation();
        }
    }

    private async Task SavePublicTabsAsync()
    {
        if (!CanSavePublicTabs())
        {
            Notice = AppStrings.Get("Settings_PublicTabsSynchronizeBeforeSave");
            return;
        }

        try
        {
            var currentSettings = _settings.GetSettings();
            _settings.SaveSettings(currentSettings with
            {
                AccountName = Normalize(AccountName),
                League = Normalize(SelectedLeague),
            });
            await _publicTabsSetup.SaveAsync(CreateCurrentPublicTabsResult());

            _configuredPublicTabLabels.Clear();
            foreach (var tab in PublicTabs.Where(tab => tab.IsIncluded))
            {
                _configuredPublicTabLabels.Add(tab.Label);
            }

            _configuredAccountName = Normalize(AccountName);
            _configuredLeague = Normalize(SelectedLeague);
            _verifiedPublicTabLabels.Clear();
            _verifiedPublicTabLabels.UnionWith(_configuredPublicTabLabels);
            _latestPublicTabResults.Clear();
            Notice = AppStrings.Get("Settings_PublicTabsSaved");
            RefreshPublicTabPresentation();
        }
        catch (Exception exception)
        {
            Notice = AppStrings.Format("Settings_PublicTabsSaveFailedFormat", exception.Message);
        }
    }

    private PublicTabsSetupRequest CreatePublicTabsRequest() => new(
        Normalize(AccountName),
        Normalize(SelectedLeague),
        PublicTabs.Select(tab => new PublicTabsSetupTab(
            tab.Label,
            tab.RequiredName,
            tab.PriceAmount,
            tab.PriceCurrency,
            tab.IsIncluded)).ToArray());

    private PublicTabsSynchronizationResult CreateCurrentPublicTabsResult()
    {
        var tabs = PublicTabs.Select(tab =>
        {
            var setupTab = new PublicTabsSetupTab(
                tab.Label,
                tab.RequiredName,
                tab.PriceAmount,
                tab.PriceCurrency,
                tab.IsIncluded);
            if (!tab.IsIncluded)
            {
                return new PublicTabSynchronizationResult(
                    setupTab,
                    PublicTabSynchronizationStatus.Excluded,
                    AppStrings.Get("Settings_PublicTabDisabled"));
            }

            return new PublicTabSynchronizationResult(
                setupTab,
                _verifiedPublicTabLabels.Contains(tab.Label)
                    ? PublicTabSynchronizationStatus.Synchronized
                    : PublicTabSynchronizationStatus.Error,
                tab.Detail);
        }).ToArray();

        return new PublicTabsSynchronizationResult(Normalize(AccountName), Normalize(SelectedLeague), tabs);
    }

    private void OnPublicTabPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!string.IsNullOrEmpty(eventArgs.PropertyName) &&
            !string.Equals(eventArgs.PropertyName, nameof(PublicTabMarkerViewModel.IsIncluded), StringComparison.Ordinal))
        {
            return;
        }

        RefreshPublicTabPresentation();
    }

    private void OnPublicTabsInputsChanged()
    {
        _latestPublicTabResults.Clear();
        _verifiedPublicTabLabels.Clear();
        if (HasConfiguredAccountAndLeague)
        {
            _verifiedPublicTabLabels.UnionWith(_configuredPublicTabLabels);
        }

        RefreshPublicTabPresentation();
    }

    private void RefreshPublicTabPresentation()
    {
        var hasConfiguredAccountAndLeague = HasConfiguredAccountAndLeague;
        foreach (var tab in PublicTabs)
        {
            if (!tab.IsIncluded)
            {
                var wasConfigured = hasConfiguredAccountAndLeague && _configuredPublicTabLabels.Contains(tab.Label);
                tab.SetState(
                    AppStrings.Get(wasConfigured ? "Settings_PublicTabPendingDisable" : "Settings_PublicTabDisabled"),
                    AppStrings.Get(wasConfigured ? "Settings_PublicTabPendingDisableDetail" : "Settings_PublicTabDisabledDetail"),
                    false);
                continue;
            }

            if (_verifiedPublicTabLabels.Contains(tab.Label))
            {
                var isAlreadyConfigured = hasConfiguredAccountAndLeague && _configuredPublicTabLabels.Contains(tab.Label);
                tab.SetState(
                    AppStrings.Get(isAlreadyConfigured ? "Settings_PublicTabConnected" : "Settings_PublicTabReady"),
                    AppStrings.Get(isAlreadyConfigured ? "Settings_PublicTabConnectedDetail" : "Settings_PublicTabReadyDetail"),
                    true);
                continue;
            }

            if (_latestPublicTabResults.TryGetValue(tab.Label, out var result))
            {
                tab.SetState(GetSynchronizationStatus(result.Status), result.RussianSummary, false);
                continue;
            }

            tab.SetState(
                AppStrings.Get("Settings_PublicTabPendingSynchronization"),
                AppStrings.Get("Settings_PublicTabPendingSynchronizationDetail"),
                false);
        }

        OnPropertyChanged(nameof(PublicTabsSummary));
        UpdatePublicTabsCommandAvailability();
    }

    private bool CanSynchronizePublicTabs() =>
        !IsSynchronizingPublicTabs &&
        !string.IsNullOrWhiteSpace(AccountName) &&
        !string.IsNullOrWhiteSpace(SelectedLeague) &&
        PublicTabs.Any(tab => tab.IsIncluded);

    private bool CanSavePublicTabs() =>
        !IsSynchronizingPublicTabs &&
        HasPublicTabsChanges &&
        PublicTabs.Any(tab => tab.IsIncluded) &&
        PublicTabs.Where(tab => tab.IsIncluded).All(tab => _verifiedPublicTabLabels.Contains(tab.Label));

    private bool HasPublicTabsChanges =>
        !HasConfiguredAccountAndLeague ||
        !_configuredPublicTabLabels.SetEquals(PublicTabs.Where(tab => tab.IsIncluded).Select(tab => tab.Label));

    private bool HasConfiguredAccountAndLeague =>
        !string.IsNullOrWhiteSpace(_configuredAccountName) &&
        !string.IsNullOrWhiteSpace(_configuredLeague) &&
        string.Equals(_configuredAccountName, Normalize(AccountName), StringComparison.Ordinal) &&
        string.Equals(_configuredLeague, Normalize(SelectedLeague), StringComparison.Ordinal);

    private void CancelPublicTabsSynchronization() => _publicTabsSynchronizationCancellation?.Cancel();

    private void UpdatePublicTabsCommandAvailability()
    {
        _synchronizePublicTabsCommand.RaiseCanExecuteChanged();
        _savePublicTabsCommand.RaiseCanExecuteChanged();
        _cancelPublicTabsSynchronizationCommand.RaiseCanExecuteChanged();
    }

    private static string GetSynchronizationStatus(PublicTabSynchronizationStatus status) => status switch
    {
        PublicTabSynchronizationStatus.NotFound => AppStrings.Get("InitialSetup_NotFound"),
        PublicTabSynchronizationStatus.WrongTabName => AppStrings.Get("InitialSetup_WrongTab"),
        PublicTabSynchronizationStatus.Ambiguous => AppStrings.Get("InitialSetup_Ambiguous"),
        PublicTabSynchronizationStatus.Excluded => AppStrings.Get("InitialSetup_Excluded"),
        _ => AppStrings.Get("Common_Error"),
    };

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();
}
