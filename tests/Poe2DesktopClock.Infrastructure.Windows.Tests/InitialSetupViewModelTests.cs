using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.ViewModels;
using Xunit;

namespace Poe2DesktopClock.Infrastructure.Windows.Tests;

public sealed class InitialSetupViewModelTests
{
    [Fact]
    public async Task Existing_complete_state_skips_the_one_time_setup_without_loading_leagues()
    {
        var state = new TestSetupStateStore(new InitialSetupState(
            InitialSetupState.CurrentSchemaVersion,
            InitialSetupState.CurrentSetupVersion,
            InitialSetupStep.DeviceConnection));
        var leagues = new TestLeagueCatalog();
        var viewModel = CreateViewModel(
            state,
            leagues,
            new CurrencySetupStatus(true, true, "ready"),
            hasSavedPublicConfiguration: true);

        var requiresSetup = await viewModel.InitializeAsync();

        Assert.False(requiresSetup);
        Assert.Equal(0, leagues.Calls);
    }

    [Fact]
    public async Task Existing_legacy_configuration_is_marked_complete_and_skips_the_setup()
    {
        var state = new TestSetupStateStore(InitialSetupState.NotStarted);
        var viewModel = CreateViewModel(
            state,
            new TestLeagueCatalog(),
            new CurrencySetupStatus(true, true, "ready"),
            hasSavedPublicConfiguration: true);

        var requiresSetup = await viewModel.InitializeAsync();

        Assert.False(requiresSetup);
        Assert.True(state.Value.IsCompleted);
    }

    [Fact]
    public async Task Incomplete_new_configuration_opens_the_public_tabs_step_after_currency_is_ready()
    {
        var viewModel = CreateViewModel(
            new TestSetupStateStore(InitialSetupState.NotStarted),
            new TestLeagueCatalog(),
            new CurrencySetupStatus(true, true, "ready"),
            hasSavedPublicConfiguration: false);

        var requiresSetup = await viewModel.InitializeAsync();

        Assert.True(requiresSetup);
        Assert.True(viewModel.IsPublicTabsStep);
        Assert.False(viewModel.IsDeviceStep);
    }

    [Fact]
    public async Task Interrupted_setup_at_the_device_step_is_resumed_instead_of_being_migrated_as_legacy()
    {
        var state = new TestSetupStateStore(new InitialSetupState(
            InitialSetupState.CurrentSchemaVersion,
            CompletedVersion: 0,
            InitialSetupStep.DeviceConnection));
        var viewModel = CreateViewModel(
            state,
            new TestLeagueCatalog(),
            new CurrencySetupStatus(true, true, "ready"),
            hasSavedPublicConfiguration: true);

        var requiresSetup = await viewModel.InitializeAsync();

        Assert.True(requiresSetup);
        Assert.True(viewModel.IsDeviceStep);
        Assert.False(state.Value.IsCompleted);
    }

    private static InitialSetupViewModel CreateViewModel(
        TestSetupStateStore state,
        TestLeagueCatalog leagues,
        CurrencySetupStatus currencyStatus,
        bool hasSavedPublicConfiguration) =>
        new(
            state,
            new TestTrackerSettingsUseCase(),
            leagues,
            new TestCurrencySetupUseCase(currencyStatus),
            new TestPublicTabsSetupUseCase(hasSavedPublicConfiguration));

    private sealed class TestSetupStateStore : IInitialSetupStateStore
    {
        public TestSetupStateStore(InitialSetupState value) => Value = value;

        public InitialSetupState Value { get; private set; }

        public InitialSetupState Get() => Value;

        public void Save(InitialSetupState state) => Value = state;
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
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetPoe2LeaguesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(["league"]);
        }
    }

    private sealed class TestCurrencySetupUseCase : ICurrencySetupUseCase
    {
        private readonly CurrencySetupStatus _status;

        public TestCurrencySetupUseCase(CurrencySetupStatus status) => _status = status;

        public CurrencySetupStatus GetCurrencySetupStatus() => _status;

        public Task SelectCurrencyRegionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CalibrateCurrencySlotsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestPublicTabsSetupUseCase : IPublicTabsSetupUseCase
    {
        private readonly bool _hasSavedConfiguration;

        public TestPublicTabsSetupUseCase(bool hasSavedConfiguration) =>
            _hasSavedConfiguration = hasSavedConfiguration;

        public IReadOnlyList<PublicTabsSetupTab> GetTabs() =>
        [
            new PublicTabsSetupTab("tab", "~price 1001 mirror", 1001m, "mirror", true),
        ];

        public bool HasSavedConfiguration() => _hasSavedConfiguration;

        public Task<PublicTabsSynchronizationResult> SynchronizeAsync(
            PublicTabsSetupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublicTabsSynchronizationResult(request.AccountName, request.League, []));

        public Task SaveAsync(
            PublicTabsSynchronizationResult synchronization,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
