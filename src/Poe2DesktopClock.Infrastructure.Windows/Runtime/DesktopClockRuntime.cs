using System.Diagnostics;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Interop;
using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.PublicStash;
using Poe2DeskTracker.Regions;
using Poe2DesktopClock.Core.Interfaces;
using Poe2DesktopClock.Core.Models;
using Poe2DesktopClock.Infrastructure.Windows.Monitoring;
using Poe2DesktopClock.Infrastructure.Windows.Settings;

namespace Poe2DesktopClock.Infrastructure.Windows.Runtime;

/// <summary>
/// Собирает Windows-адаптеры в единый сценарий приложения. UI получает только
/// снимки, статусы и русские сводки, не зная о Win32, OCR и Trade API.
/// </summary>
public sealed class DesktopClockRuntime : IClockRuntime
{
    private const string CurrencyRegionName = "currency";
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly SemaphoreSlim _publicScanLock = new(1, 1);
    private readonly PoeProcessLocator _processLocator = new();
    private readonly WindowsGraphicsCaptureService _captureService = new();
    private readonly TradeApiClient _tradeApiClient = new();
    private readonly PoeNinjaPriceClient _priceClient = new();
    private readonly RegionStore _regionStore;
    private readonly CurrencyLayoutStore _currencyLayoutStore;
    private readonly PublicStashSettingsStore _publicStashSettingsStore;
    private readonly DesktopSettingsStore _settingsStore;
    private readonly string _previewPath;
    private readonly string _liveFramePath;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private CurrencyScanValue? _latestCurrency;
    private PublicScanValue? _latestPublic;
    private ClockSnapshot? _latestSnapshot;
    private ClockMonitorStatus _monitorStatus = ClockMonitorStatus.Stopped;
    private int _isAutomaticPublicRefreshRunning;

    public DesktopClockRuntime()
    {
        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DeskTracker");
        var desktopDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Poe2DesktopClock");
        var cacheDirectory = Path.Combine(desktopDirectory, "cache");

        _regionStore = new RegionStore(Path.Combine(legacyDirectory, "regions.json"));
        _currencyLayoutStore = new CurrencyLayoutStore(Path.Combine(legacyDirectory, "currency-layouts.json"));
        _publicStashSettingsStore = new PublicStashSettingsStore(Path.Combine(legacyDirectory, "public-stash.json"));
        _settingsStore = new DesktopSettingsStore(Path.Combine(desktopDirectory, "settings.json"));
        _previewPath = Path.Combine(cacheDirectory, "currency-preview.png");
        _liveFramePath = Path.Combine(cacheDirectory, "currency-live.png");
    }

    public event EventHandler<ClockSnapshot>? ClockSnapshotChanged;

    public event EventHandler<ClockMonitorStatus>? MonitorStatusChanged;

    public TrackerSettings GetSettings()
    {
        var publicSettings = _publicStashSettingsStore.Get();
        var fallback = TrackerSettings.Default with
        {
            AccountName = publicSettings?.AccountName ?? string.Empty,
            League = publicSettings?.League ?? string.Empty,
        };
        return _settingsStore.Get(fallback);
    }

    public void SaveSettings(TrackerSettings settings)
    {
        var normalized = settings.Normalize();
        _settingsStore.Save(normalized);

        var existing = _publicStashSettingsStore.Get();
        var markers = existing is { HasCompleteMarkers: true }
            ? existing.TabMarkers!
            : PublicTabMarkerCatalog.CreateDefaultMarkers().ToList();
        _publicStashSettingsStore.Save(new PublicStashSettings(
            normalized.AccountName,
            normalized.League,
            [],
            new List<PublicStashTabMarker>(markers)));
    }

    public GameStatus GetGameStatus()
    {
        var gameWindow = _processLocator.FindGameWindow();
        return gameWindow is null
            ? new GameStatus(false, "Path of Exile 2 не найден или свёрнут.")
            : new GameStatus(
                true,
                $"Path of Exile 2 найден: {gameWindow.Width}×{gameWindow.Height}.",
                gameWindow.ProcessId,
                gameWindow.Width,
                gameWindow.Height);
    }

    public CurrencySetupStatus GetCurrencySetupStatus()
    {
        var prerequisites = GetCurrencyPrerequisites();
        if (prerequisites is null)
        {
            var hasRegion = _regionStore.GetAll().Any(region => string.Equals(region.Name, CurrencyRegionName, StringComparison.OrdinalIgnoreCase));
            return hasRegion
                ? new CurrencySetupStatus(true, false, "Область Currency выбрана. Нужно откалибровать ячейки.")
                : new CurrencySetupStatus(false, false, "Выберите область Currency-вкладки в настройках.");
        }

        return new CurrencySetupStatus(true, true, "Currency-вкладка готова к отслеживанию.");
    }

    public Task<IReadOnlyList<string>> GetPoe2LeaguesAsync(CancellationToken cancellationToken = default) =>
        _tradeApiClient.GetPoe2LeagueNamesAsync(cancellationToken);

    public async Task SelectCurrencyRegionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gameWindow = _processLocator.FindGameWindow(includeMinimized: true)
            ?? throw new InvalidOperationException("Path of Exile 2 не найден. Запустите игру и повторите выбор области.");

        Win32Native.RestoreAndActivateWindow(gameWindow.Handle);
        if (!await WaitForStableClientBoundsAsync(gameWindow.Handle, TimeSpan.FromSeconds(2), cancellationToken))
        {
            throw new InvalidOperationException("Окно игры не готово к выбору области. Сделайте его видимым и повторите попытку.");
        }

        var region = await RegionSelectionOverlay.SelectAsync(gameWindow.Handle, CurrencyRegionName);
        if (region is null)
        {
            throw new OperationCanceledException("Выбор области Currency-вкладки отменён.");
        }

        _regionStore.Clear();
        _currencyLayoutStore.Clear();
        _regionStore.Upsert(region);
        SetMonitorStatus(ClockMonitorStatus.NeedsSetup);
    }

    public async Task CalibrateCurrencySlotsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prerequisites = GetCurrencyPrerequisites(requireLayout: false)
            ?? throw new InvalidOperationException("Сначала выберите область Currency-вкладки.");
        var gameWindow = _processLocator.FindGameWindow()
            ?? throw new InvalidOperationException("Path of Exile 2 не найден или свёрнут.");

        Directory.CreateDirectory(Path.GetDirectoryName(_previewPath)!);
        await _captureService.SaveRegionAsync(gameWindow.Handle, prerequisites.Region, _previewPath, TimeSpan.FromSeconds(5));
        var detectedSlots = CurrencyTabProfile.Apply(CurrencyGridDetector.Detect(_previewPath));
        if (detectedSlots.Count == 0)
        {
            throw new InvalidOperationException("Не удалось найти ячейки. Откройте Currency-вкладку и повторите калибровку.");
        }

        var layout = await CurrencyCalibrationForm.CalibrateAsync(
            _previewPath,
            prerequisites.Region.Name,
            detectedSlots,
            _currencyLayoutStore.Get(prerequisites.Region.Name));
        if (layout is null)
        {
            throw new OperationCanceledException("Калибровка Currency-вкладки отменена.");
        }

        _currencyLayoutStore.Upsert(layout);
        SetMonitorStatus(ClockMonitorStatus.Stopped);
    }

    public async Task<ClockSnapshot> RefreshAsync(
        bool refreshPublicTabs,
        IProgress<TrackerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _scanLock.WaitAsync(cancellationToken);
        try
        {
            var settings = GetSettings();
            var prices = await LoadPricesAsync(settings, progress, cancellationToken);
            progress?.Report(new TrackerProgress("Считываю Currency-вкладку..."));
            var currency = await ScanCurrentCurrencyAsync(prices, cancellationToken);
            if (currency is not null)
            {
                _latestCurrency = currency;
            }

            if (refreshPublicTabs)
            {
                _latestPublic = await ScanPublicTabsAsync(settings, prices, progress, cancellationToken);
            }

            return PublishSnapshot(prices?.RetrievedAt);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    public Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_monitorTask is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorCurrencyAsync(_monitorCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopCurrencyMonitoringAsync()
    {
        var cancellation = Interlocked.Exchange(ref _monitorCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (_monitorTask is not null)
            {
                await _monitorTask;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when a user closes the desktop application.
        }
        finally
        {
            cancellation.Dispose();
            _monitorTask = null;
            SetMonitorStatus(ClockMonitorStatus.Stopped);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopCurrencyMonitoringAsync();
        _scanLock.Dispose();
        _publicScanLock.Dispose();
        _priceClient.Dispose();
        _tradeApiClient.Dispose();
        _captureService.Dispose();
    }

    private async Task MonitorCurrencyAsync(CancellationToken cancellationToken)
    {
        string? lastFingerprint = null;
        var lastVisibilityCheck = DateTimeOffset.MinValue;
        var isCurrencyTabVisible = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var settings = GetSettings();
                var period = TimeSpan.FromSeconds(1d / settings.CurrencyScreensPerSecond);
                var prerequisites = GetCurrencyPrerequisites();
                if (prerequisites is null)
                {
                    SetMonitorStatus(ClockMonitorStatus.NeedsSetup);
                    await Task.Delay(period, cancellationToken);
                    continue;
                }

                var gameWindow = _processLocator.FindGameWindow();
                if (gameWindow is null)
                {
                    SetMonitorStatus(ClockMonitorStatus.WaitingForGame);
                    await Task.Delay(period, cancellationToken);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_liveFramePath)!);
                await _captureService.SaveRegionAsync(gameWindow.Handle, prerequisites.Region, _liveFramePath, TimeSpan.FromSeconds(2));
                var fingerprint = CurrencyFrameFingerprint.Create(_liveFramePath, prerequisites.Layout);
                var contentsChanged = !string.Equals(fingerprint, lastFingerprint, StringComparison.Ordinal);
                var now = DateTimeOffset.UtcNow;
                var shouldCheckVisibility = contentsChanged || now - lastVisibilityCheck >= TimeSpan.FromSeconds(1);
                if (shouldCheckVisibility)
                {
                    lastVisibilityCheck = now;
                    lastFingerprint = fingerprint;
                    var detectedSlots = CurrencyTabProfile.Apply(CurrencyGridDetector.Detect(_liveFramePath));
                    isCurrencyTabVisible = detectedSlots.Count >= prerequisites.Layout.Slots.Count;
                    if (!isCurrencyTabVisible)
                    {
                        SetMonitorStatus(ClockMonitorStatus.WaitingForCurrencyTab);
                    }
                    else if (contentsChanged || _latestCurrency is null)
                    {
                        var prices = await LoadPricesAsync(settings, null, cancellationToken);
                        var currency = await ScanCurrencyFrameAsync(_liveFramePath, prerequisites.Layout, prices);
                        _latestCurrency = currency;
                        PublishSnapshot(prices?.RetrievedAt);
                        SetMonitorStatus(ClockMonitorStatus.Tracking);
                    }
                    else
                    {
                        SetMonitorStatus(ClockMonitorStatus.Tracking);
                    }
                }
                else if (isCurrencyTabVisible)
                {
                    SetMonitorStatus(ClockMonitorStatus.Tracking);
                }
                else
                {
                    SetMonitorStatus(ClockMonitorStatus.WaitingForCurrencyTab);
                }

                StartAutomaticPublicRefreshIfDue(settings, cancellationToken);
                await Task.Delay(period, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            SetMonitorStatus(ClockMonitorStatus.Error);
        }
    }

    private CurrencyPrerequisites? GetCurrencyPrerequisites(bool requireLayout = true)
    {
        var region = _regionStore.GetAll()
            .FirstOrDefault(saved => string.Equals(saved.Name, CurrencyRegionName, StringComparison.OrdinalIgnoreCase));
        if (region is null)
        {
            return null;
        }

        var layout = _currencyLayoutStore.Get(region.Name);
        if (layout is null || layout.Slots.Count == 0)
        {
            return requireLayout ? null : new CurrencyPrerequisites(region, new CurrencyLayout(region.Name, 0, 0, []));
        }

        return new CurrencyPrerequisites(region, layout);
    }

    private async Task<CurrencyScanValue?> ScanCurrentCurrencyAsync(PoeNinjaPriceSnapshot? prices, CancellationToken cancellationToken)
    {
        var prerequisites = GetCurrencyPrerequisites();
        var gameWindow = _processLocator.FindGameWindow();
        if (prerequisites is null || gameWindow is null)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_previewPath)!);
        await _captureService.SaveRegionAsync(gameWindow.Handle, prerequisites.Region, _previewPath, TimeSpan.FromSeconds(5));
        return await ScanCurrencyFrameAsync(_previewPath, prerequisites.Layout, prices);
    }

    private static async Task<CurrencyScanValue> ScanCurrencyFrameAsync(
        string imagePath,
        CurrencyLayout layout,
        PoeNinjaPriceSnapshot? prices)
    {
        var amounts = await CurrencyAmountScanner.ScanAsync(imagePath, layout);
        var totalDivines = 0m;
        var unreadableSlots = 0;
        var unpricedItems = 0;
        foreach (var amount in amounts.Where(amount => amount.Amount is null || amount.Amount > 0))
        {
            if (amount.Amount is null)
            {
                unreadableSlots++;
                continue;
            }

            if (prices is null ||
                !CurrencyTabProfile.TryGetPoeNinjaName(amount.Name, out var priceName) ||
                !prices.TryGetDivinePrice(priceName, out var unitDivines))
            {
                unpricedItems++;
                continue;
            }

            totalDivines += unitDivines * amount.Amount.Value;
        }

        return new CurrencyScanValue(totalDivines, unpricedItems, unreadableSlots, DateTimeOffset.UtcNow);
    }

    private async Task<PublicScanValue> ScanPublicTabsAsync(
        TrackerSettings settings,
        PoeNinjaPriceSnapshot? prices,
        IProgress<TrackerProgress>? progress,
        CancellationToken cancellationToken)
    {
        await _publicScanLock.WaitAsync(cancellationToken);
        try
        {
            return await ScanPublicTabsCoreAsync(settings, prices, progress, cancellationToken);
        }
        finally
        {
            _publicScanLock.Release();
        }
    }

    private async Task<PublicScanValue> ScanPublicTabsCoreAsync(
        TrackerSettings settings,
        PoeNinjaPriceSnapshot? prices,
        IProgress<TrackerProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountName) || string.IsNullOrWhiteSpace(settings.League))
        {
            return new PublicScanValue(0m, 0, false, DateTimeOffset.UtcNow, "Не заполнены имя аккаунта или лига для публичных вкладок.");
        }

        var savedSettings = _publicStashSettingsStore.Get();
        var markers = savedSettings is { HasCompleteMarkers: true }
            ? savedSettings.TabMarkers!
            : PublicTabMarkerCatalog.CreateDefaultMarkers();
        var discovery = await _tradeApiClient.DiscoverPublicTabItemsAsync(
            settings.AccountName,
            settings.League,
            markers,
            new Progress<PublicStashSearchProgress>(item =>
                progress?.Report(new TrackerProgress($"Публичные вкладки: {item.CompletedGroups}/{item.TotalGroups} — {item.Label}.", item.CompletedGroups, item.TotalGroups))),
            new Progress<PublicStashFetchProgress>(item =>
                progress?.Report(new TrackerProgress($"Загружаю предметы: пачка {item.CurrentBatch}/{item.TotalBatches}.", item.CurrentBatch, item.TotalBatches))),
            cancellationToken);

        var tabNames = markers.Select(marker => marker.TabName).ToHashSet(StringComparer.Ordinal);
        var selectedItems = discovery.Items
            .Where(item => tabNames.Contains(item.TabName))
            .GroupBy(item => item.Id ?? $"{item.TabName}\u001f{item.X}\u001f{item.Y}\u001f{item.ItemName}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var totalDivines = 0m;
        var unpricedItems = 0;
        foreach (var itemGroup in selectedItems.GroupBy(item => item.ItemName, StringComparer.Ordinal))
        {
            if (prices is null || !prices.TryGetDivinePrice(itemGroup.Key, out var unitDivines))
            {
                unpricedItems++;
                continue;
            }

            totalDivines += unitDivines * itemGroup.Sum(item => item.StackSize);
        }

        var isComplete = !discovery.IsTruncated;
        foreach (var marker in markers)
        {
            var markerItems = discovery.Items
                .Where(item => string.Equals(item.MarkerLabel, marker.Label, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (markerItems.Any(item => !string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal)) ||
                !selectedItems.Any(item => string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal)))
            {
                isComplete = false;
            }
        }

        var summary = isComplete
            ? $"Публичные вкладки: {selectedItems.Length} стаков, оценка обновлена."
            : "Публичные вкладки прочитаны частично: проверьте предупреждения и названия вкладок.";
        return new PublicScanValue(totalDivines, unpricedItems, isComplete, DateTimeOffset.UtcNow, summary);
    }

    private void StartAutomaticPublicRefreshIfDue(TrackerSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsAutomaticPublicRefreshEnabled ||
            !settings.IsCurrencyMonitoringEnabled ||
            (_latestPublic is not null && DateTimeOffset.UtcNow - _latestPublic.UpdatedAt < TimeSpan.FromMinutes(settings.PublicRefreshIntervalMinutes)) ||
            Interlocked.CompareExchange(ref _isAutomaticPublicRefreshRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var prices = await LoadPricesAsync(settings, null, cancellationToken);
                _latestPublic = await ScanPublicTabsAsync(settings, prices, null, cancellationToken);
                PublishSnapshot(prices?.RetrievedAt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The desktop application is closing.
            }
            catch
            {
                // The current value remains visible; the monitor status is reserved for capture/OCR failures.
            }
            finally
            {
                Interlocked.Exchange(ref _isAutomaticPublicRefreshRunning, 0);
            }
        }, CancellationToken.None);
    }

    private async Task<PoeNinjaPriceSnapshot?> LoadPricesAsync(
        TrackerSettings settings,
        IProgress<TrackerProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.League))
        {
            return null;
        }

        progress?.Report(new TrackerProgress("Обновляю цены в Divine..."));
        return await _priceClient.GetPricesAsync(
            settings.League,
            TimeSpan.FromMinutes(settings.PriceRefreshIntervalMinutes),
            cancellationToken);
    }

    private ClockSnapshot PublishSnapshot(DateTimeOffset? pricesUpdatedAt)
    {
        var currency = _latestCurrency;
        var publicTabs = _latestPublic;
        var total = (currency?.Divines ?? 0m) + (publicTabs?.Divines ?? 0m);
        var isComplete = currency is { UnpricedItems: 0, UnreadableSlots: 0 } && publicTabs is { IsComplete: true };
        var publicSummary = publicTabs?.RussianSummary ?? "Публичные вкладки ещё не были обновлены.";
        var summary = isComplete
            ? $"Итого {total:0.##} Divine. Currency-вкладка и публичные вкладки актуальны."
            : $"Итого {total:0.##} Divine — частичная оценка. {publicSummary}";

        var snapshot = new ClockSnapshot(
            total,
            currency?.Divines ?? 0m,
            publicTabs?.Divines ?? 0m,
            currency?.UpdatedAt,
            publicTabs?.UpdatedAt,
            pricesUpdatedAt,
            isComplete,
            summary);
        _latestSnapshot = snapshot;
        ClockSnapshotChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    private void SetMonitorStatus(ClockMonitorStatus status)
    {
        if (_monitorStatus == status)
        {
            return;
        }

        _monitorStatus = status;
        MonitorStatusChanged?.Invoke(this, status);
    }

    private static async Task<bool> WaitForStableClientBoundsAsync(nint windowHandle, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32Native.IsIconic(windowHandle) &&
                Win32Native.TryGetClientBoundsOnScreen(windowHandle, out var left, out var top, out var width, out var height))
            {
                await Task.Delay(150, cancellationToken);
                if (!Win32Native.IsIconic(windowHandle) &&
                    Win32Native.TryGetClientBoundsOnScreen(windowHandle, out var confirmedLeft, out var confirmedTop, out var confirmedWidth, out var confirmedHeight) &&
                    left == confirmedLeft && top == confirmedTop && width == confirmedWidth && height == confirmedHeight)
                {
                    return true;
                }
            }
            else
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return false;
    }
}
