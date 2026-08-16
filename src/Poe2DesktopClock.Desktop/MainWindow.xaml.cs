using System.Windows;
using Poe2DesktopClock.Infrastructure.Windows.Runtime;
using Poe2DesktopClock.Desktop.Services;
using Poe2DesktopClock.Desktop.ViewModels;

namespace Poe2DesktopClock.Desktop;

public partial class MainWindow : Window
{
    private readonly RuntimeTrackerStatusProvider _statusProvider;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var runtime = new DesktopClockRuntime();
        _statusProvider = new RuntimeTrackerStatusProvider(runtime);
        _viewModel = new MainViewModel(
            new DashboardViewModel(_statusProvider),
            new SettingsViewModel(runtime));
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await _viewModel.Settings.LoadAsync();
        if (_viewModel.Settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
        }

        await _statusProvider.InitializeAsync();
    }

    private async void OnClosed(object? sender, EventArgs eventArgs)
    {
        await _statusProvider.DisposeAsync();
    }
}
