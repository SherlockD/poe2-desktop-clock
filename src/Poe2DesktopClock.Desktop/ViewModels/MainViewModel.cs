using System.Windows.Input;
using Poe2DesktopClock.Desktop.Infrastructure;

namespace Poe2DesktopClock.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private object _currentPage;
    private bool _isDashboardSelected = true;
    private bool _isSettingsSelected;
    private bool _isInitialSetupActive = true;
    private bool _isApplicationReady;

    public MainViewModel(
        DashboardViewModel dashboard,
        SettingsViewModel settings,
        InitialSetupViewModel initialSetup)
    {
        Dashboard = dashboard;
        Settings = settings;
        InitialSetup = initialSetup;
        Dashboard.PropertyChanged += OnDashboardPropertyChanged;
        InitialSetup.SetupCompleted += OnInitialSetupCompleted;
        _currentPage = dashboard;
        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
    }

    public DashboardViewModel Dashboard { get; }

    public SettingsViewModel Settings { get; }

    public InitialSetupViewModel InitialSetup { get; }

    public event EventHandler? InitialSetupCompleted;

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

    public bool IsInitialSetupActive
    {
        get => _isInitialSetupActive;
        private set => SetProperty(ref _isInitialSetupActive, value);
    }

    public bool IsApplicationReady
    {
        get => _isApplicationReady;
        private set => SetProperty(ref _isApplicationReady, value);
    }

    public string GameStatus => Dashboard.GameStatus;

    public ICommand ShowDashboardCommand { get; }

    public ICommand ShowSettingsCommand { get; }

    /// <summary>
    /// Decides whether this launch must show the one-time setup. Returning
    /// <c>false</c> means the normal shell is ready and App may start the
    /// tracker runtime.
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        var requiresInitialSetup = await InitialSetup.InitializeAsync();
        if (!requiresInitialSetup)
        {
            ActivateApplicationShell();
        }

        return requiresInitialSetup;
    }

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

    private void OnInitialSetupCompleted(object? sender, EventArgs eventArgs)
    {
        ActivateApplicationShell();
        InitialSetupCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ActivateApplicationShell()
    {
        IsInitialSetupActive = false;
        IsApplicationReady = true;
        ShowDashboard();
    }

    private void OnDashboardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName) ||
            string.Equals(eventArgs.PropertyName, nameof(DashboardViewModel.GameStatus), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(GameStatus));
        }
    }
}
