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
    private Task? _runtimeInitializationTask;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        var services = Poe2DesktopClockComposition.CreateServiceCollection();
        services.AddSingleton<ITrackerStatusProvider, RuntimeTrackerStatusProvider>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<InitialSetupViewModel>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();
        _statusProvider = _services.GetRequiredService<ITrackerStatusProvider>();
        var viewModel = _services.GetRequiredService<MainViewModel>();
        viewModel.InitialSetupCompleted += OnInitialSetupCompleted;
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
        var requiresInitialSetup = await viewModel.InitializeAsync();
        if (!requiresInitialSetup)
        {
            await InitializeRuntimeAsync(viewModel, mainWindow, applyStartMinimized: true);
        }
    }

    private async void OnInitialSetupCompleted(object? sender, EventArgs eventArgs)
    {
        if (sender is not MainViewModel viewModel || MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        await InitializeRuntimeAsync(viewModel, mainWindow, applyStartMinimized: false);
    }

    private Task InitializeRuntimeAsync(
        MainViewModel viewModel,
        MainWindow mainWindow,
        bool applyStartMinimized) =>
        _runtimeInitializationTask ??= InitializeRuntimeCoreAsync(viewModel, mainWindow, applyStartMinimized);

    private async Task InitializeRuntimeCoreAsync(
        MainViewModel viewModel,
        MainWindow mainWindow,
        bool applyStartMinimized)
    {
        // Loading saved settings applies local state before its first await;
        // remote league loading may proceed while the tracker starts.
        var settingsLoadTask = viewModel.Settings.LoadAsync();
        await _statusProvider!.InitializeAsync();
        await settingsLoadTask;
        if (applyStartMinimized && viewModel.Settings.StartMinimized)
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

        if (sender is MainWindow setupWindow &&
            setupWindow.DataContext is MainViewModel { IsInitialSetupActive: true } &&
            MessageBox.Show(
                "Настройка ещё не завершена. Закрыть приложение? Прогресс первых шагов сохранится.",
                "Первоначальная настройка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            eventArgs.Cancel = true;
            return;
        }

        eventArgs.Cancel = true;
        _isShuttingDown = true;
        try
        {
            if (sender is MainWindow { DataContext: MainViewModel viewModel })
            {
                viewModel.InitialSetup.CancelPendingOperations();
            }

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
