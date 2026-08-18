using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Poe2DesktopClock.Composition;
using Poe2DesktopClock.Desktop.Services;
using Poe2DesktopClock.Desktop.ViewModels;

namespace Poe2DesktopClock.Desktop;

public partial class App : System.Windows.Application
{
    private ITrackerStatusProvider? _statusProvider;
    private IServiceProvider? _services;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        var services = Poe2DesktopClockComposition.CreateServiceCollection();
        services.AddSingleton<ITrackerStatusProvider, RuntimeTrackerStatusProvider>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();
        _statusProvider = _services.GetRequiredService<ITrackerStatusProvider>();
        var viewModel = _services.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow(viewModel);
        MainWindow = mainWindow;
        mainWindow.Loaded += OnMainWindowLoaded;
        mainWindow.Closing += OnMainWindowClosing;
        mainWindow.Show();
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs eventArgs)
    {
        var mainWindow = (MainWindow)sender;
        var viewModel = (MainViewModel)mainWindow.DataContext;
        // Loading saved settings is synchronous up to its first await; refresh
        // the remote league list in parallel so session monitoring starts as
        // close to game launch as possible.
        var settingsLoadTask = viewModel.Settings.LoadAsync();
        await _statusProvider!.InitializeAsync();
        await settingsLoadTask;
        if (viewModel.Settings.StartMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
        }
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_isShuttingDown)
        {
            return;
        }

        eventArgs.Cancel = true;
        _isShuttingDown = true;
        try
        {
            if (_services is IAsyncDisposable services)
            {
                await services.DisposeAsync();
                _services = null;
            }
        }
        finally
        {
            if (sender is MainWindow mainWindow)
            {
                mainWindow.Closing -= OnMainWindowClosing;
                mainWindow.Close();
            }
        }
    }
}
