using System.Collections.ObjectModel;
using System.Windows.Input;
using Poe2DesktopClock.Core.Interfaces;
using Poe2DesktopClock.Core.Models;
using Poe2DesktopClock.Desktop.Infrastructure;

namespace Poe2DesktopClock.Desktop.ViewModels;

/// <summary>
/// Редактируемые настройки desktop-приложения и обычные пользовательские шаги
/// подготовки Currency-вкладки — без диагностических действий.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IClockRuntime _runtime;
    private string _accountName = string.Empty;
    private string _selectedLeague = string.Empty;
    private bool _isCurrencyTrackingEnabled = true;
    private bool _isAutomaticPublicRefreshEnabled;
    private int _selectedCaptureFps = 2;
    private int _publicRefreshMinutes = 15;
    private int _priceRefreshMinutes = 5;
    private bool _startMinimized;
    private string _notice = "Настройте источник данных и подтвердите изменения.";

    public SettingsViewModel(IClockRuntime runtime)
    {
        _runtime = runtime;
        CaptureFrequencies = new ObservableCollection<int>([2, 3]);
        Leagues = [];
        PublicTabs = new ObservableCollection<PublicTabMarkerViewModel>(CreateDefaultPublicTabs());
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

    public bool IsAutomaticPublicRefreshEnabled
    {
        get => _isAutomaticPublicRefreshEnabled;
        set => SetProperty(ref _isAutomaticPublicRefreshEnabled, value);
    }

    public int SelectedCaptureFps
    {
        get => _selectedCaptureFps;
        set => SetProperty(ref _selectedCaptureFps, value);
    }

    public int PublicRefreshMinutes
    {
        get => _publicRefreshMinutes;
        set => SetProperty(ref _publicRefreshMinutes, Math.Max(10, value));
    }

    public int PriceRefreshMinutes
    {
        get => _priceRefreshMinutes;
        set => SetProperty(ref _priceRefreshMinutes, Math.Max(1, value));
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
        ApplySettings(_runtime.GetSettings());
        await RefreshLeaguesAsync();
    }

    private async Task SaveAsync()
    {
        var settings = new TrackerSettings(
            AccountName,
            SelectedLeague,
            SelectedCaptureFps,
            IsCurrencyTrackingEnabled,
            IsAutomaticPublicRefreshEnabled,
            PublicRefreshMinutes,
            PriceRefreshMinutes,
            StartMinimized);
        _runtime.SaveSettings(settings);
        if (settings.IsCurrencyMonitoringEnabled)
        {
            await _runtime.StartCurrencyMonitoringAsync();
        }
        else
        {
            await _runtime.StopCurrencyMonitoringAsync();
        }

        Notice = "Настройки сохранены. Currency-вкладка использует выбранную частоту кадров.";
    }

    private async Task RefreshLeaguesAsync()
    {
        try
        {
            var leagues = await _runtime.GetPoe2LeaguesAsync();
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
            await _runtime.SelectCurrencyRegionAsync();
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
            await _runtime.CalibrateCurrencySlotsAsync();
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
        IsAutomaticPublicRefreshEnabled = settings.IsAutomaticPublicRefreshEnabled;
        PublicRefreshMinutes = settings.PublicRefreshIntervalMinutes;
        PriceRefreshMinutes = settings.PriceRefreshIntervalMinutes;
        StartMinimized = settings.StartMinimized;
    }

    private static IEnumerable<PublicTabMarkerViewModel> CreateDefaultPublicTabs()
    {
        var labels = new[] { "Разлом", "Бездна", "Ритуал", "Экспедиция", "Делириум", "Сущности", "Руны", "Фрагменты" };
        return labels.Select((label, index) => new PublicTabMarkerViewModel(label, $"~price {1001 + index} mirror"));
    }
}
