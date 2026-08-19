using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Composition;
using Poe2DesktopClock.Desktop.Localization;
using Poe2DesktopClock.Desktop.Services;
using Poe2DesktopClock.Desktop.ViewModels;

namespace Poe2DesktopClock.Desktop;

public partial class App : System.Windows.Application
{
    private ITrackerStatusProvider? _statusProvider;
    private ISystemTrayIcon? _trayIcon;
    private IServiceProvider? _services;
    private Task? _runtimeInitializationTask;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        var services = Poe2DesktopClockComposition.CreateServiceCollection();
        services.AddSingleton<ITrackerStatusProvider, RuntimeTrackerStatusProvider>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<PublicTabsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<InitialSetupViewModel>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();
        _statusProvider = _services.GetRequiredService<ITrackerStatusProvider>();
        _trayIcon = _services.GetRequiredService<ISystemTrayIcon>();
        _trayIcon.RestoreRequested += OnTrayRestoreRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;
        var viewModel = _services.GetRequiredService<MainViewModel>();
        viewModel.InitialSetupCompleted += OnInitialSetupCompleted;
        viewModel.FullResetRequested += OnFullResetRequested;
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

    private async void OnFullResetRequested(object? sender, EventArgs eventArgs)
    {
        if (sender is not MainViewModel viewModel || MainWindow is not MainWindow mainWindow || _isShuttingDown)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            AppStrings.Get("App_FullResetConfirmation"),
            AppStrings.Get("Settings_FullReset"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await viewModel.ResetApplicationAsync();
            await DisposeServicesAsync();
            RestartCurrentProcess();
            _isShuttingDown = true;
            mainWindow.Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                AppStrings.Format("App_FullResetFailedFormat", exception.Message),
                AppStrings.Get("Settings_FullReset"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

    private void OnMainWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_isShuttingDown)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (sender is MainWindow mainWindow)
        {
            mainWindow.Hide();
            _trayIcon?.Show();
        }
    }

    private void OnTrayRestoreRequested(object? sender, EventArgs eventArgs)
    {
        _ = Dispatcher.BeginInvoke(RestoreMainWindow);
    }

    private void RestoreMainWindow()
    {
        if (MainWindow is not MainWindow mainWindow || _isShuttingDown)
        {
            return;
        }

        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
        _trayIcon?.Hide();
    }

    private void OnTrayExitRequested(object? sender, EventArgs eventArgs)
    {
        _ = Dispatcher.BeginInvoke(ShutdownApplicationAsync);
    }

    private async Task ShutdownApplicationAsync()
    {
        if (_isShuttingDown || MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        if (mainWindow.DataContext is MainViewModel { IsInitialSetupActive: true } &&
            MessageBox.Show(
                AppStrings.Get("App_InitialSetupExitConfirmation"),
                AppStrings.Get("InitialSetup_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _isShuttingDown = true;
        _trayIcon?.Hide();
        if (mainWindow.DataContext is MainViewModel viewModel)
        {
            viewModel.InitialSetup.CancelPendingOperations();
            viewModel.Settings.CancelPendingOperations();
        }

        await DisposeServicesAsync();
        mainWindow.Close();
    }

    private async Task DisposeServicesAsync()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.RestoreRequested -= OnTrayRestoreRequested;
            _trayIcon.ExitRequested -= OnTrayExitRequested;
            _trayIcon = null;
        }

        if (_services is IAsyncDisposable services)
        {
            await services.DisposeAsync();
            _services = null;
        }
    }

    private static void RestartCurrentProcess()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(AppStrings.Get("App_ExecutablePathUnavailable"));
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = true,
        };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException(AppStrings.Get("App_RestartFailed"));
        }
    }
}
