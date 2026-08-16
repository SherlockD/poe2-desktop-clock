using System.Windows.Input;
using Poe2DesktopClock.Desktop.Infrastructure;

namespace Poe2DesktopClock.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private object _currentPage;
    private bool _isDashboardSelected = true;
    private bool _isSettingsSelected;

    public MainViewModel(DashboardViewModel dashboard, SettingsViewModel settings)
    {
        Dashboard = dashboard;
        Settings = settings;
        _currentPage = dashboard;
        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
    }

    public DashboardViewModel Dashboard { get; }

    public SettingsViewModel Settings { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public bool IsDashboardSelected
    {
        get => _isDashboardSelected;
        private set => SetProperty(ref _isDashboardSelected, value);
    }

    public bool IsSettingsSelected
    {
        get => _isSettingsSelected;
        private set => SetProperty(ref _isSettingsSelected, value);
    }

    public string GameStatus => Dashboard.CurrencyStatus;

    public ICommand ShowDashboardCommand { get; }

    public ICommand ShowSettingsCommand { get; }

    private void ShowDashboard()
    {
        CurrentPage = Dashboard;
        IsDashboardSelected = true;
        IsSettingsSelected = false;
        Dashboard.Refresh();
        OnPropertyChanged(nameof(GameStatus));
    }

    private void ShowSettings()
    {
        CurrentPage = Settings;
        IsDashboardSelected = false;
        IsSettingsSelected = true;
    }
}
