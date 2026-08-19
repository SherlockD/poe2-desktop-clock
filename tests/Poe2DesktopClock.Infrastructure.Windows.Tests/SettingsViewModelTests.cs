using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.ViewModels;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Load_exposes_enabled_and_disabled_public_tabs()
    {
        var publicTabs = new TestPublicTabsSetupUseCase();
        var viewModel = CreateViewModel(publicTabs);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.PublicTabs.Count);
        Assert.True(viewModel.PublicTabs[0].IsIncluded);
        Assert.False(viewModel.PublicTabs[1].IsIncluded);
    }

    [Fact]
    public async Task Disabling_a_configured_tab_can_be_saved_without_another_trade_api_check()
    {
        var publicTabs = new TestPublicTabsSetupUseCase(firstTabSelected: true, secondTabSelected: true);
        var viewModel = CreateViewModel(publicTabs);
        await viewModel.LoadAsync();

        viewModel.PublicTabs[1].IsIncluded = false;

        Assert.True(viewModel.SavePublicTabsCommand.CanExecute(null));
        viewModel.SavePublicTabsCommand.Execute(null);
        var saved = await publicTabs.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, publicTabs.SynchronizeCalls);
        Assert.Single(saved.Tabs, tab => tab.Tab.IsSelected && tab.IsSynchronized);
        Assert.Single(saved.Tabs, tab => !tab.Tab.IsSelected && tab.Status == PublicTabSynchronizationStatus.Excluded);
    }

    [Fact]
    public async Task Enabling_a_new_tab_requires_successful_synchronization_before_save()
    {
        var publicTabs = new TestPublicTabsSetupUseCase();
        var viewModel = CreateViewModel(publicTabs);
        await viewModel.LoadAsync();

        viewModel.PublicTabs[1].IsIncluded = true;

        Assert.False(viewModel.SavePublicTabsCommand.CanExecute(null));
        viewModel.SynchronizePublicTabsCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.SavePublicTabsCommand.CanExecute(null));
        viewModel.SavePublicTabsCommand.Execute(null);
        var saved = await publicTabs.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, publicTabs.SynchronizeCalls);
        Assert.Equal(2, saved.SynchronizedCount);
    }

    private static SettingsViewModel CreateViewModel(TestPublicTabsSetupUseCase publicTabs) => new(
        new TestTrackerSettingsUseCase(),
        new TestLeagueCatalog(),
        new TestCurrencySetupUseCase(),
        new TestMonitoringUseCase(),
        publicTabs);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TestTrackerSettingsUseCase : ITrackerSettingsUseCase
    {
        private TrackerSettings _settings = TrackerSettings.Default with
        {
            AccountName = "account",
            League = "league",
        };

        public TrackerSettings GetSettings() => _settings;

        public void SaveSettings(TrackerSettings settings) => _settings = settings;
    }

    private sealed class TestLeagueCatalog : ILeagueCatalog
    {
        public Task<IReadOnlyList<string>> GetPoe2LeaguesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["league"]);
    }

    private sealed class TestCurrencySetupUseCase : ICurrencySetupUseCase
    {
        public CurrencySetupStatus GetCurrencySetupStatus() => new(true, true, "ready");

        public Task SelectCurrencyRegionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CalibrateCurrencySlotsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestMonitoringUseCase : ITrackerMonitoringUseCase
    {
        public event EventHandler<ClockMonitorStatus>? MonitorStatusChanged
        {
            add { }
            remove { }
        }

        public GameStatus GetGameStatus() => new(false, "not running");

        public Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopCurrencyMonitoringAsync() => Task.CompletedTask;
    }

    private sealed class TestPublicTabsSetupUseCase : IPublicTabsSetupUseCase
    {
        private readonly IReadOnlyList<PublicTabsSetupTab> _tabs;

        public TestPublicTabsSetupUseCase(bool firstTabSelected = true, bool secondTabSelected = false)
        {
            _tabs =
            [
                new PublicTabsSetupTab("first", "~price 1001 mirror", 1001m, "mirror", firstTabSelected),
                new PublicTabsSetupTab("second", "~price 1002 mirror", 1002m, "mirror", secondTabSelected),
            ];
        }

        public int SynchronizeCalls { get; private set; }

        public TaskCompletionSource<PublicTabsSynchronizationResult> Saved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<PublicTabsSetupTab> GetTabs() => _tabs;

        public bool HasSavedConfiguration() => true;

        public Task<PublicTabsSynchronizationResult> SynchronizeAsync(
            PublicTabsSetupRequest request,
            CancellationToken cancellationToken = default)
        {
            SynchronizeCalls++;
            return Task.FromResult(new PublicTabsSynchronizationResult(
                request.AccountName,
                request.League,
                request.Tabs.Select(tab => new PublicTabSynchronizationResult(
                    tab,
                    tab.IsSelected
                        ? PublicTabSynchronizationStatus.Synchronized
                        : PublicTabSynchronizationStatus.Excluded,
                    "ok")).ToArray()));
        }

        public Task SaveAsync(
            PublicTabsSynchronizationResult synchronization,
            CancellationToken cancellationToken = default)
        {
            Saved.TrySetResult(synchronization);
            return Task.CompletedTask;
        }
    }
}
