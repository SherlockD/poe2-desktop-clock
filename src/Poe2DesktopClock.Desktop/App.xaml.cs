using System.ComponentModel;
using System.Diagnostics;
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
            "Будут удалены настройки, калибровка Currency-вкладки, публичные вкладки и сохранённые оценки. Приложение перезапустится и откроет онбординг с первого шага.",
            "Полный сброс",
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
                $"Не удалось полностью сбросить приложение: {exception.Message}",
                "Полный сброс",
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

            await DisposeServicesAsync();
        }
        finally
        {
            if (sender is MainWindow mainWindow)
            {
                mainWindow.Closing -= OnMainWindowClosing;
                // DisposeAsync can complete synchronously. Calling Close in
                // that case would re-enter WPF's current Closing event and
                // throws "Cannot ... Close ... while a Window is closing".
                // Queue the final close after the cancelled event returns.
                _ = mainWindow.Dispatcher.BeginInvoke(
                    new Action(mainWindow.Close),
                    System.Windows.Threading.DispatcherPriority.Normal);
            }
        }
    }

    private async Task DisposeServicesAsync()
    {
        if (_services is IAsyncDisposable services)
        {
            await services.DisposeAsync();
            _services = null;
        }
    }

    private static void RestartCurrentProcess()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу.");
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
            throw new InvalidOperationException("Не удалось перезапустить приложение.");
        }
    }
}
