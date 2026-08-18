using System.Globalization;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Application.Models;
using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>
/// Verifies public-tab marker placement before the markers are enabled for
/// background valuation. Each marker is queried independently so one failed
/// Trade API request does not hide outcomes for the remaining tabs.
/// </summary>
public sealed class PublicTabsSetupUseCase : IPublicTabsSetupUseCase
{
    private readonly IPublicTabsSetupTradeGateway _tradeGateway;
    private readonly PublicStashSettingsStore _settingsStore;

    public PublicTabsSetupUseCase(
        IPublicTabsSetupTradeGateway tradeGateway,
        PublicStashSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(tradeGateway);
        ArgumentNullException.ThrowIfNull(settingsStore);
        _tradeGateway = tradeGateway;
        _settingsStore = settingsStore;
    }

    public IReadOnlyList<PublicTabsSetupTab> GetTabs()
    {
        var stored = _settingsStore.Get();
        var persistedMarkers = stored is { HasCompleteMarkers: true }
            ? stored.TabMarkers!
            : [];
        var hasPersistedSelection = persistedMarkers.Count > 0;

        return PublicTabMarkerCatalog.CreateDefaultMarkers()
            .Select(marker => new PublicTabsSetupTab(
                marker.Label,
                marker.TabName,
                marker.PriceAmount,
                marker.PriceCurrency,
                IsSelected: !hasPersistedSelection || persistedMarkers.Any(saved => SameMarker(saved, marker))))
            .ToArray();
    }

    public bool HasSavedConfiguration()
    {
        var stored = _settingsStore.Get();
        return stored is { HasCompleteMarkers: true } &&
               !string.IsNullOrWhiteSpace(stored.AccountName) &&
               !string.IsNullOrWhiteSpace(stored.League);
    }

    public async Task<PublicTabsSynchronizationResult> SynchronizeAsync(
        PublicTabsSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        var results = new List<PublicTabSynchronizationResult>(normalized.Tabs.Count);
        foreach (var tab in normalized.Tabs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tab.IsSelected)
            {
                results.Add(new PublicTabSynchronizationResult(
                    tab,
                    PublicTabSynchronizationStatus.Excluded,
                    "Вкладка исключена из синхронизации."));
                continue;
            }

            results.Add(await SynchronizeTabAsync(normalized.AccountName, normalized.League, tab, cancellationToken));
        }

        return new PublicTabsSynchronizationResult(normalized.AccountName, normalized.League, results);
    }

    public Task SaveAsync(
        PublicTabsSynchronizationResult synchronization,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(synchronization);
        ArgumentNullException.ThrowIfNull(synchronization.Tabs);

        var request = Normalize(new PublicTabsSetupRequest(
            synchronization.AccountName,
            synchronization.League,
            synchronization.Tabs.Select(result => result?.Tab ?? throw new ArgumentException(
                "Synchronization result contains an empty tab.",
                nameof(synchronization))).ToArray()));
        var successfulMarkers = new List<PublicStashTabMarker>();
        for (var index = 0; index < request.Tabs.Count; index++)
        {
            var result = synchronization.Tabs[index];
            if (request.Tabs[index].IsSelected && result.Status == PublicTabSynchronizationStatus.Synchronized)
            {
                var tab = request.Tabs[index];
                successfulMarkers.Add(new PublicStashTabMarker(
                    tab.Label,
                    tab.TabName,
                    tab.PriceAmount,
                    tab.PriceCurrency));
            }
        }

        if (successfulMarkers.Count == 0)
        {
            throw new InvalidOperationException("Не удалось сохранить публичные вкладки: нет успешно синхронизированных вкладок.");
        }

        _settingsStore.Save(new PublicStashSettings(
            request.AccountName,
            request.League,
            [],
            successfulMarkers));
        return Task.CompletedTask;
    }

    private async Task<PublicTabSynchronizationResult> SynchronizeTabAsync(
        string accountName,
        string league,
        PublicTabsSetupTab tab,
        CancellationToken cancellationToken)
    {
        try
        {
            var marker = new PublicStashTabMarker(tab.Label, tab.TabName, tab.PriceAmount, tab.PriceCurrency);
            var search = await _tradeGateway.SearchAsync(accountName, league, marker, cancellationToken);
            if (search.IsTruncated)
            {
                return Result(
                    tab,
                    PublicTabSynchronizationStatus.Ambiguous,
                    "Trade API вернул неполную выдачу по маркеру. Уберите повторяющуюся цену и повторите синхронизацию.");
            }

            if (search.TotalMatches == 0 || search.ItemIds.Count == 0)
            {
                return Result(
                    tab,
                    PublicTabSynchronizationStatus.NotFound,
                    "Маркер не найден. Проверьте публичность вкладки, точное имя и наличие маркерного предмета.");
            }

            var items = await _tradeGateway.FetchAsync(search, cancellationToken);
            var markerItems = items
                .Where(item => string.Equals(item.MarkerLabel, tab.Label, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (markerItems.Length == 0)
            {
                return Result(
                    tab,
                    PublicTabSynchronizationStatus.NotFound,
                    "Trade API не вернул предметы по маркеру. Проверьте вкладку и повторите синхронизацию.");
            }

            var tabNames = markerItems
                .Select(item => item.TabName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var stashIdentities = markerItems
                .Select(GetStashIdentity)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (tabNames.Length > 1 || stashIdentities.Length > 1)
            {
                return Result(
                    tab,
                    PublicTabSynchronizationStatus.Ambiguous,
                    "Маркер найден в нескольких вкладках. Уберите повторяющуюся цену и повторите синхронизацию.");
            }

            if (!string.Equals(tabNames[0], tab.TabName, StringComparison.Ordinal))
            {
                return Result(
                    tab,
                    PublicTabSynchronizationStatus.WrongTabName,
                    $"Маркер найден во вкладке «{tabNames[0]}», ожидается «{tab.TabName}».");
            }

            return Result(tab, PublicTabSynchronizationStatus.Synchronized, "Вкладка синхронизирована.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result(
                tab,
                PublicTabSynchronizationStatus.Error,
                $"Не удалось синхронизировать вкладку: {GetUserFacingError(exception)}");
        }
    }

    private static PublicTabsSetupRequest Normalize(PublicTabsSetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var accountName = (request.AccountName ?? string.Empty).Trim();
        var league = (request.League ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new ArgumentException("Укажите имя аккаунта.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(league))
        {
            throw new ArgumentException("Укажите лигу.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Tabs);
        var tabs = request.Tabs.Select(NormalizeTab).ToArray();
        if (tabs.Length == 0 || !tabs.Any(tab => tab.IsSelected))
        {
            throw new ArgumentException("Выберите хотя бы одну публичную вкладку.", nameof(request));
        }

        if (tabs.Select(tab => tab.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tabs.Length ||
            tabs.Select(tab => tab.TabName).Distinct(StringComparer.Ordinal).Count() != tabs.Length ||
            tabs.Select(tab => $"{tab.PriceAmount.ToString(CultureInfo.InvariantCulture)}\u001f{tab.PriceCurrency}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != tabs.Length)
        {
            throw new ArgumentException("Названия, имена и цены маркеров публичных вкладок должны быть уникальны.", nameof(request));
        }

        return new PublicTabsSetupRequest(accountName, league, tabs);
    }

    private static PublicTabsSetupTab NormalizeTab(PublicTabsSetupTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var label = (tab.Label ?? string.Empty).Trim();
        var tabName = (tab.TabName ?? string.Empty).Trim();
        var priceCurrency = (tab.PriceCurrency ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(label) ||
            string.IsNullOrWhiteSpace(tabName) ||
            tab.PriceAmount <= 0 ||
            string.IsNullOrWhiteSpace(priceCurrency))
        {
            throw new ArgumentException("Маркер публичной вкладки заполнен некорректно.", nameof(tab));
        }

        return new PublicTabsSetupTab(label, tabName, tab.PriceAmount, priceCurrency, tab.IsSelected);
    }

    private static PublicTabSynchronizationResult Result(
        PublicTabsSetupTab tab,
        PublicTabSynchronizationStatus status,
        string summary) => new(tab, status, summary);

    private static bool SameMarker(PublicStashTabMarker left, PublicStashTabMarker right) =>
        string.Equals(left.Label, right.Label, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.TabName, right.TabName, StringComparison.Ordinal) &&
        left.PriceAmount == right.PriceAmount &&
        string.Equals(left.PriceCurrency, right.PriceCurrency, StringComparison.OrdinalIgnoreCase);

    private static string GetStashIdentity(PublicStashItem item) =>
        !string.IsNullOrWhiteSpace(item.StashId)
            ? $"id:{item.StashId}"
            : $"name:{item.TabName}";

    private static string GetUserFacingError(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? "неизвестная ошибка Trade API"
            : exception.Message;
}
