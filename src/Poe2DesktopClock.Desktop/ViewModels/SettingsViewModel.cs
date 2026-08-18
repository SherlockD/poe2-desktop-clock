using System.Collections.ObjectModel;
using System.Windows.Input;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.Infrastructure;

namespace Poe2DesktopClock.Desktop.ViewModels;

/// <summary>
/// Редактируемые настройки desktop-приложения и обычные пользовательские шаги
/// подготовки Currency-вкладки — без диагностических действий.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ITrackerSettingsUseCase _settings;
    private readonly ILeagueCatalog _leagueCatalog;
    private readonly ICurrencySetupUseCase _currencySetup;
    private readonly ITrackerMonitoringUseCase _monitoring;
    private readonly IPublicTabsSetupUseCase _publicTabsSetup;
    private string _accountName = string.Empty;
    private string _selectedLeague = string.Empty;
    private bool _isCurrencyTrackingEnabled = true;
    private int _selectedCaptureFps = 2;
    private bool _startMinimized;
    private string _notice = "Настройте источник данных и подтвердите изменения.";

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
    }

    public ObservableCollection<int> CaptureFrequencies { get; }

    public ObservableCollection<string> Leagues { get; }

    public ObservableCollection<PublicTabMarkerViewModel> PublicTabs { get; }

    public string AccountName
    {
        get => _accountName;
        set => SetProperty(ref _accountName, value);
    }

    public string SelectedLeague
    {
        get => _selectedLeague;
        set => SetProperty(ref _selectedLeague, value);
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

    public string Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    public ICommand SaveCommand { get; }

    public ICommand RefreshLeaguesCommand { get; }

    public ICommand SelectCurrencyAreaCommand { get; }

    public ICommand CalibrateCurrencyCommand { get; }

    public async Task LoadAsync()
    {
        ApplySettings(_settings.GetSettings());
        LoadConfiguredPublicTabs();
        await RefreshLeaguesAsync();
    }

    private async Task SaveAsync()
    {
        var settings = new TrackerSettings(
            AccountName,
            SelectedLeague,
            SelectedCaptureFps,
            IsCurrencyTrackingEnabled,
            IsAutomaticPublicRefreshEnabled: true,
            PublicRefreshIntervalMinutes: 2,
            PriceRefreshIntervalMinutes: 30,
            StartMinimized);
        _settings.SaveSettings(settings);
        await _monitoring.StopCurrencyMonitoringAsync();
        await _monitoring.StartCurrencyMonitoringAsync();

        Notice = "Настройки сохранены. Currency-вкладка использует выбранную частоту кадров.";
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

            Notice = "Список актуальных лиг обновлён.";
        }
        catch (Exception exception)
        {
            Notice = $"Не удалось обновить лиги: {exception.Message}";
        }
    }

    private async Task SelectCurrencyAreaAsync()
    {
        try
        {
            await _currencySetup.SelectCurrencyRegionAsync();
            Notice = "Область выбрана. Теперь нажмите «Проверить ячейки».";
        }
        catch (OperationCanceledException)
        {
            Notice = "Выбор области отменён.";
        }
        catch (Exception exception)
        {
            Notice = $"Не удалось выбрать область: {exception.Message}";
        }
    }

    private async Task CalibrateCurrencyAsync()
    {
        try
        {
            await _currencySetup.CalibrateCurrencySlotsAsync();
            Notice = "Ячейки Currency-вкладки сохранены. Отслеживание можно включить.";
        }
        catch (OperationCanceledException)
        {
            Notice = "Калибровка отменена.";
        }
        catch (Exception exception)
        {
            Notice = $"Не удалось проверить ячейки: {exception.Message}";
        }
    }

    private void ApplySettings(TrackerSettings settings)
    {
        AccountName = settings.AccountName;
        SelectedLeague = settings.League;
        IsCurrencyTrackingEnabled = settings.IsCurrencyMonitoringEnabled;
        SelectedCaptureFps = settings.CurrencyScreensPerSecond;
        StartMinimized = settings.StartMinimized;
    }

    private void LoadConfiguredPublicTabs()
    {
        PublicTabs.Clear();
        foreach (var tab in _publicTabsSetup.GetTabs().Where(tab => tab.IsSelected))
        {
            PublicTabs.Add(new PublicTabMarkerViewModel(tab.Label, tab.TabName));
        }
    }
}
