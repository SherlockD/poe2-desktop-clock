using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Desktop.Infrastructure;

namespace Poe2DesktopClock.Desktop.ViewModels;

/// <summary>
/// One-time three-step setup shown before the tracker starts monitoring.
/// It coordinates focused application ports but owns only WPF presentation
/// state and Russian user-facing text.
/// </summary>
public sealed class InitialSetupViewModel : ViewModelBase
{
    private readonly IInitialSetupStateStore _stateStore;
    private readonly ITrackerSettingsUseCase _settings;
    private readonly ILeagueCatalog _leagueCatalog;
    private readonly ICurrencySetupUseCase _currencySetup;
    private readonly IPublicTabsSetupUseCase _publicTabsSetup;
    private readonly AsyncRelayCommand _selectCurrencyAreaCommand;
    private readonly AsyncRelayCommand _calibrateCurrencyCommand;
    private readonly AsyncRelayCommand _currencyNextCommand;
    private readonly RelayCommand _publicTabsBackCommand;
    private readonly AsyncRelayCommand _refreshLeaguesCommand;
    private readonly AsyncRelayCommand _synchronizePublicTabsCommand;
    private readonly RelayCommand _cancelSynchronizationCommand;
    private readonly AsyncRelayCommand _publicTabsNextCommand;
    private readonly RelayCommand _deviceBackCommand;
    private readonly AsyncRelayCommand _finishCommand;
    private InitialSetupState _state = InitialSetupState.NotStarted;
    private CurrencySetupStatus? _currencyStatus;
    private InitialSetupStep _currentStep = InitialSetupStep.CurrencyTab;
    private PublicTabsSynchronizationResult? _synchronization;
    private CancellationTokenSource? _synchronizationCancellation;
    private string _accountName = string.Empty;
    private string _selectedLeague = string.Empty;
    private string _currencyAreaStatus = "Область ещё не выбрана.";
    private string _currencySlotsStatus = "Ячейки ещё не настроены.";
    private string _notice = "Настройте Currency-вкладку, затем продолжите.";
    private bool _isSynchronizing;
    private bool _hasSavedPublicConfiguration;
    private bool _initialized;

    public InitialSetupViewModel(
        IInitialSetupStateStore stateStore,
        ITrackerSettingsUseCase settings,
        ILeagueCatalog leagueCatalog,
        ICurrencySetupUseCase currencySetup,
        IPublicTabsSetupUseCase publicTabsSetup)
    {
        _stateStore = stateStore;
        _settings = settings;
        _leagueCatalog = leagueCatalog;
        _currencySetup = currencySetup;
        _publicTabsSetup = publicTabsSetup;

        Leagues = [];
        PublicTabs = [];
        _selectCurrencyAreaCommand = new AsyncRelayCommand(SelectCurrencyAreaAsync);
        _calibrateCurrencyCommand = new AsyncRelayCommand(CalibrateCurrencyAsync, () => HasCurrencyArea);
        _currencyNextCommand = new AsyncRelayCommand(MoveToPublicTabsAsync, () => IsCurrencyReady);
        _publicTabsBackCommand = new RelayCommand(() => SetCurrentStep(InitialSetupStep.CurrencyTab));
        _refreshLeaguesCommand = new AsyncRelayCommand(RefreshLeaguesAsync, () => IsNotSynchronizing);
        _synchronizePublicTabsCommand = new AsyncRelayCommand(SynchronizePublicTabsAsync, CanSynchronizePublicTabs);
        _cancelSynchronizationCommand = new RelayCommand(CancelSynchronization, () => IsSynchronizing);
        _publicTabsNextCommand = new AsyncRelayCommand(SavePublicTabsAndMoveToDeviceAsync, CanMoveToDevice);
        _deviceBackCommand = new RelayCommand(() => SetCurrentStep(InitialSetupStep.PublicTabs));
        _finishCommand = new AsyncRelayCommand(FinishAsync);
    }

    public event EventHandler? SetupCompleted;

    public ObservableCollection<string> Leagues { get; }

    public ObservableCollection<InitialSetupPublicTabViewModel> PublicTabs { get; }

    public string AccountName
    {
        get => _accountName;
        set
        {
            if (SetProperty(ref _accountName, value))
            {
                InvalidateAllPublicTabsSynchronization();
            }
        }
    }

    public string SelectedLeague
    {
        get => _selectedLeague;
        set
        {
            if (SetProperty(ref _selectedLeague, value))
            {
                InvalidateAllPublicTabsSynchronization();
            }
        }
    }

    public string CurrencyAreaStatus
    {
        get => _currencyAreaStatus;
        private set => SetProperty(ref _currencyAreaStatus, value);
    }

    public string CurrencySlotsStatus
    {
        get => _currencySlotsStatus;
        private set => SetProperty(ref _currencySlotsStatus, value);
    }

    public string Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    public bool IsCurrencyStep => _currentStep == InitialSetupStep.CurrencyTab;

    public bool IsPublicTabsStep => _currentStep == InitialSetupStep.PublicTabs;

    public bool IsDeviceStep => _currentStep == InitialSetupStep.DeviceConnection;

    public bool IsSynchronizing
    {
        get => _isSynchronizing;
        private set
        {
            if (!SetProperty(ref _isSynchronizing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsNotSynchronizing));
            UpdateCommandAvailability();
        }
    }

    public bool IsNotSynchronizing => !IsSynchronizing;

    public string CurrencyStepState => IsCurrencyReady
        ? "Готово"
        : IsCurrencyStep
            ? "В процессе"
            : "Не начат";

    public string PublicTabsStepState => CanMoveToDevice() ||
                                         (IsDeviceStep && _hasSavedPublicConfiguration)
        ? "Готово"
        : IsPublicTabsStep
            ? "В процессе"
            : "Не начат";

    public string DeviceStepState => _state.IsCompleted
        ? "Готово"
        : IsDeviceStep
            ? "В процессе"
            : "Не начат";

    public string PublicTabsSummary => _synchronization is null
        ? $"Выбрано вкладок: {PublicTabs.Count(tab => tab.IsIncluded)} из {PublicTabs.Count}."
        : $"Синхронизировано {_synchronization.SynchronizedCount} из {_synchronization.SelectedCount} выбранных вкладок.";

    public string DeviceSummary =>
        $"Currency-вкладка готова; синхронизировано публичных вкладок: {_synchronization?.SynchronizedCount ?? PublicTabs.Count(tab => tab.IsIncluded)}.";

    public ICommand SelectCurrencyAreaCommand => _selectCurrencyAreaCommand;

    public ICommand CalibrateCurrencyCommand => _calibrateCurrencyCommand;

    public ICommand CurrencyNextCommand => _currencyNextCommand;

    public ICommand PublicTabsBackCommand => _publicTabsBackCommand;

    public ICommand RefreshLeaguesCommand => _refreshLeaguesCommand;

    public ICommand SynchronizePublicTabsCommand => _synchronizePublicTabsCommand;

    public ICommand CancelSynchronizationCommand => _cancelSynchronizationCommand;

    public ICommand PublicTabsNextCommand => _publicTabsNextCommand;

    public ICommand DeviceBackCommand => _deviceBackCommand;

    public ICommand FinishCommand => _finishCommand;

    /// <summary>Called during application shutdown to stop the in-flight Trade API setup operation.</summary>
    public void CancelPendingOperations() => _synchronizationCancellation?.Cancel();

    /// <summary>
    /// Loads durable setup state. Returns <c>true</c> only when the initial
    /// setup needs to be shown; a complete legacy configuration is migrated
    /// without interrupting an existing user.
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        if (_initialized)
        {
            return !_state.IsCompleted;
        }

        _initialized = true;
        _state = _stateStore.Get();
        var settings = _settings.GetSettings();
        AccountName = settings.AccountName;
        SelectedLeague = settings.League;
        _hasSavedPublicConfiguration = _publicTabsSetup.HasSavedConfiguration();
        PopulatePublicTabs();
        RefreshCurrencyStatus();

        if (_state.IsCompleted)
        {
            return false;
        }

        if (CanMigrateExistingConfiguration(settings))
        {
            CompleteState();
            return false;
        }

        var initialStep = DetermineInitialStep();
        SetCurrentStep(initialStep, persistProgress: false);
        if (initialStep == InitialSetupStep.PublicTabs)
        {
            await RefreshLeaguesAsync();
        }

        return true;
    }

    private bool HasCurrencyArea => _currencyStatus?.HasRegion == true;

    private bool IsCurrencyReady => _currencyStatus is { HasRegion: true, HasCalibratedSlots: true };

    private async Task SelectCurrencyAreaAsync()
    {
        try
        {
            await _currencySetup.SelectCurrencyRegionAsync();
            RefreshCurrencyStatus();
            Notice = "Область сохранена. Теперь настройте ячейки и подтвердите их Enter.";
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
            await _currencySetup.CalibrateCurrencySlotsAsync();
            RefreshCurrencyStatus();
            Notice = IsCurrencyReady
                ? "Ячейки сохранены. Можно перейти к публичным вкладкам."
                : "Ячейки не были сохранены. Повторите настройку.";
        }
        catch (OperationCanceledException)
        {
            Notice = "Настройка ячеек отменена.";
        }
        catch (Exception exception)
        {
            Notice = $"Не удалось настроить ячейки: {exception.Message}";
        }
    }

    private async Task MoveToPublicTabsAsync()
    {
        if (!IsCurrencyReady)
        {
            Notice = "Сначала выберите область и сохраните настройку ячеек.";
            return;
        }

        SetCurrentStep(InitialSetupStep.PublicTabs);
        if (Leagues.Count == 0)
        {
            await RefreshLeaguesAsync();
        }
    }

    private async Task RefreshLeaguesAsync()
    {
        try
        {
            var leagues = await _leagueCatalog.GetPoe2LeaguesAsync();
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

            Notice = leagues.Count == 0
                ? "Список лиг пока пуст. Повторите загрузку."
                : "Список актуальных лиг обновлён.";
        }
        catch (Exception exception)
        {
            Notice = $"Не удалось обновить лиги: {exception.Message}";
        }
    }

    private async Task SynchronizePublicTabsAsync()
    {
        if (!CanStartPublicTabsSynchronization(out var request, out var validationMessage))
        {
            Notice = validationMessage;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _synchronizationCancellation = cancellation;
        IsSynchronizing = true;
        Notice = "Синхронизация публичных вкладок началась…";
        try
        {
            var synchronization = await _publicTabsSetup.SynchronizeAsync(request, cancellation.Token);
            _synchronization = synchronization;
            ApplySynchronization(synchronization);
            Notice = synchronization.AreAllSelectedTabsSynchronized
                ? "Все выбранные вкладки синхронизированы."
                : "Не все вкладки удалось синхронизировать. Исправьте их или исключите из списка.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Notice = "Синхронизация отменена.";
        }
        catch (Exception exception)
        {
            foreach (var tab in PublicTabs.Where(tab => tab.IsIncluded))
            {
                tab.SetSynchronizationResult("Ошибка", $"Не удалось синхронизировать вкладку: {exception.Message}", false);
            }

            _synchronization = null;
            Notice = "Синхронизация завершилась ошибкой. Повторите попытку.";
        }
        finally
        {
            if (ReferenceEquals(_synchronizationCancellation, cancellation))
            {
                _synchronizationCancellation = null;
            }

            IsSynchronizing = false;
            OnPropertyChanged(nameof(PublicTabsSummary));
            OnPropertyChanged(nameof(PublicTabsStepState));
        }
    }

    private async Task SavePublicTabsAndMoveToDeviceAsync()
    {
        if (!CanMoveToDevice())
        {
            Notice = "Синхронизируйте все выбранные вкладки или исключите неудачные строки.";
            return;
        }

        try
        {
            // TrackerSettingsService keeps its own public-stash store cache;
            // save account/league first, then persist the selected verified
            // markers so a stale settings save cannot restore defaults.
            var currentSettings = _settings.GetSettings();
            _settings.SaveSettings(currentSettings with
            {
                AccountName = AccountName.Trim(),
                League = SelectedLeague.Trim(),
            });
            await _publicTabsSetup.SaveAsync(_synchronization!);
            _hasSavedPublicConfiguration = true;
            SetCurrentStep(InitialSetupStep.DeviceConnection);
            Notice = "Источники данных сохранены.";
        }
        catch (Exception exception)
        {
            Notice = $"Не удалось сохранить публичные вкладки: {exception.Message}";
        }
    }

    private Task FinishAsync()
    {
        try
        {
            CompleteState();
            Notice = "Настройка завершена. Открываю мониторинг…";
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Notice = $"Не удалось завершить настройку: {exception.Message}";
        }

        return Task.CompletedTask;
    }

    private void CancelSynchronization() => _synchronizationCancellation?.Cancel();

    private bool CanSynchronizePublicTabs() =>
        !IsSynchronizing &&
        !string.IsNullOrWhiteSpace(AccountName) &&
        !string.IsNullOrWhiteSpace(SelectedLeague) &&
        PublicTabs.Any(tab => tab.IsIncluded);

    private bool CanMoveToDevice() =>
        !IsSynchronizing &&
        _synchronization?.AreAllSelectedTabsSynchronized == true;

    private bool CanStartPublicTabsSynchronization(
        out PublicTabsSetupRequest request,
        out string validationMessage)
    {
        var tabs = PublicTabs.Select(tab => new PublicTabsSetupTab(
            tab.Label,
            tab.RequiredName,
            tab.PriceAmount,
            tab.PriceCurrency,
            tab.IsIncluded)).ToArray();
        request = new PublicTabsSetupRequest(AccountName, SelectedLeague, tabs);
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            validationMessage = "Укажите имя аккаунта.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedLeague))
        {
            validationMessage = "Выберите лигу.";
            return false;
        }

        if (!tabs.Any(tab => tab.IsSelected))
        {
            validationMessage = "Оставьте хотя бы одну публичную вкладку для синхронизации.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private void PopulatePublicTabs()
    {
        foreach (var existing in PublicTabs)
        {
            existing.PropertyChanged -= OnPublicTabPropertyChanged;
        }

        PublicTabs.Clear();
        foreach (var tab in _publicTabsSetup.GetTabs())
        {
            var viewModel = new InitialSetupPublicTabViewModel(
                tab.Label,
                tab.TabName,
                tab.PriceAmount,
                tab.PriceCurrency,
                tab.IsSelected);
            viewModel.PropertyChanged += OnPublicTabPropertyChanged;
            PublicTabs.Add(viewModel);
        }

        OnPropertyChanged(nameof(PublicTabsSummary));
    }

    private void OnPublicTabPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName) ||
            string.Equals(eventArgs.PropertyName, nameof(InitialSetupPublicTabViewModel.IsIncluded), StringComparison.Ordinal))
        {
            UpdateSynchronizationForSelection((InitialSetupPublicTabViewModel?)sender);
        }
    }

    private void InvalidateAllPublicTabsSynchronization()
    {
        _synchronization = null;
        foreach (var tab in PublicTabs.Where(tab => tab.IsIncluded))
        {
            tab.SetSynchronizationResult("Ожидает синхронизации", string.Empty, false);
        }

        OnPropertyChanged(nameof(PublicTabsSummary));
        OnPropertyChanged(nameof(PublicTabsStepState));
        UpdateCommandAvailability();
    }

    private void UpdateSynchronizationForSelection(InitialSetupPublicTabViewModel? changedTab)
    {
        if (changedTab is not null && _synchronization is not null)
        {
            var updatedResults = _synchronization.Tabs
                .Select(result =>
                {
                    if (!string.Equals(result.Tab.Label, changedTab.Label, StringComparison.OrdinalIgnoreCase))
                    {
                        return result;
                    }

                    if (!changedTab.IsIncluded)
                    {
                        return result with
                        {
                            Tab = result.Tab with { IsSelected = false },
                            Status = PublicTabSynchronizationStatus.Excluded,
                            RussianSummary = "Вкладка исключена из синхронизации.",
                        };
                    }

                    return result with
                    {
                        Tab = result.Tab with { IsSelected = true },
                        Status = PublicTabSynchronizationStatus.Error,
                        RussianSummary = "Вкладка снова включена. Синхронизируйте её перед продолжением.",
                    };
                })
                .ToArray();
            _synchronization = _synchronization with { Tabs = updatedResults };
            ApplySynchronization(_synchronization);
            return;
        }

        OnPropertyChanged(nameof(PublicTabsSummary));
        OnPropertyChanged(nameof(PublicTabsStepState));
        UpdateCommandAvailability();
    }

    private void ApplySynchronization(PublicTabsSynchronizationResult synchronization)
    {
        var resultsByLabel = synchronization.Tabs.ToDictionary(result => result.Tab.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var tab in PublicTabs)
        {
            if (!resultsByLabel.TryGetValue(tab.Label, out var result))
            {
                tab.SetSynchronizationResult("Ошибка", "Нет результата синхронизации для вкладки.", false);
                continue;
            }

            var (status, isSynchronized) = result.Status switch
            {
                PublicTabSynchronizationStatus.Synchronized => ("Синхронизирована", true),
                PublicTabSynchronizationStatus.NotFound => ("Не найдена", false),
                PublicTabSynchronizationStatus.WrongTabName => ("Неверная вкладка", false),
                PublicTabSynchronizationStatus.Ambiguous => ("Неоднозначно", false),
                PublicTabSynchronizationStatus.Excluded => ("Исключена", false),
                _ => ("Ошибка", false),
            };
            tab.SetSynchronizationResult(status, result.RussianSummary, isSynchronized);
        }

        OnPropertyChanged(nameof(PublicTabsSummary));
        OnPropertyChanged(nameof(PublicTabsStepState));
        UpdateCommandAvailability();
    }

    private void RefreshCurrencyStatus()
    {
        _currencyStatus = _currencySetup.GetCurrencySetupStatus();
        CurrencyAreaStatus = _currencyStatus.HasRegion
            ? "Область выбрана."
            : "Область ещё не выбрана.";
        CurrencySlotsStatus = _currencyStatus.HasCalibratedSlots
            ? "Ячейки сохранены."
            : "Откройте Currency-вкладку и настройте ячейки. Подтвердите результат Enter.";
        OnPropertyChanged(nameof(CurrencyStepState));
        UpdateCommandAvailability();
    }

    private InitialSetupStep DetermineInitialStep()
    {
        if (!IsCurrencyReady)
        {
            return InitialSetupStep.CurrencyTab;
        }

        if (_state.LastVisitedStep == InitialSetupStep.DeviceConnection &&
            _hasSavedPublicConfiguration &&
            HasAccountAndLeague())
        {
            return InitialSetupStep.DeviceConnection;
        }

        return InitialSetupStep.PublicTabs;
    }

    private bool CanMigrateExistingConfiguration(TrackerSettings settings) =>
        _state == InitialSetupState.NotStarted &&
        IsCurrencyReady &&
        !string.IsNullOrWhiteSpace(settings.AccountName) &&
        !string.IsNullOrWhiteSpace(settings.League) &&
        _hasSavedPublicConfiguration;

    private bool HasAccountAndLeague() =>
        !string.IsNullOrWhiteSpace(AccountName) &&
        !string.IsNullOrWhiteSpace(SelectedLeague);

    private void SetCurrentStep(InitialSetupStep step, bool persistProgress = true)
    {
        if (_currentStep == step)
        {
            return;
        }

        _currentStep = step;
        if (persistProgress && !_state.IsCompleted)
        {
            _state = new InitialSetupState(
                InitialSetupState.CurrentSchemaVersion,
                CompletedVersion: 0,
                step);
            _stateStore.Save(_state);
        }

        OnPropertyChanged(nameof(IsCurrencyStep));
        OnPropertyChanged(nameof(IsPublicTabsStep));
        OnPropertyChanged(nameof(IsDeviceStep));
        OnPropertyChanged(nameof(CurrencyStepState));
        OnPropertyChanged(nameof(PublicTabsStepState));
        OnPropertyChanged(nameof(DeviceStepState));
        OnPropertyChanged(nameof(DeviceSummary));
        UpdateCommandAvailability();
    }

    private void CompleteState()
    {
        _state = new InitialSetupState(
            InitialSetupState.CurrentSchemaVersion,
            InitialSetupState.CurrentSetupVersion,
            InitialSetupStep.DeviceConnection);
        _stateStore.Save(_state);
        OnPropertyChanged(nameof(DeviceStepState));
    }

    private void UpdateCommandAvailability()
    {
        _calibrateCurrencyCommand.RaiseCanExecuteChanged();
        _currencyNextCommand.RaiseCanExecuteChanged();
        _refreshLeaguesCommand.RaiseCanExecuteChanged();
        _synchronizePublicTabsCommand.RaiseCanExecuteChanged();
        _cancelSynchronizationCommand.RaiseCanExecuteChanged();
        _publicTabsNextCommand.RaiseCanExecuteChanged();
    }
}
