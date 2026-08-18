using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Infrastructure.Storage.PublicStash;
using Poe2DeskTracker.PublicStash;
using Xunit;

namespace Poe2DesktopClock.Contracts.Tests;

public sealed class PublicTabsSetupUseCaseTests
{
    [Fact]
    public void GetTabs_defaults_to_selected_without_claiming_a_saved_configuration()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var useCase = new PublicTabsSetupUseCase(
                new FakeTradeGateway(),
                new PublicStashSettingsStore(Path.Combine(directory, "public-stash.json")));

            Assert.False(useCase.HasSavedConfiguration());
            Assert.All(useCase.GetTabs(), tab => Assert.True(tab.IsSelected));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_classifies_selected_tabs_and_does_not_query_excluded_tabs()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var synchronized = Tab("Synced", isSelected: true);
            var notFound = Tab("Missing", isSelected: true);
            var wrongName = Tab("Wrong", isSelected: true);
            var ambiguous = Tab("Ambiguous", isSelected: true);
            var failed = Tab("Failed", isSelected: true);
            var excluded = Tab("Excluded", isSelected: false);
            var gateway = new FakeTradeGateway(
                searches:
                [
                    Search(synchronized, total: 1, ["item-synced"]),
                    Search(notFound, total: 0, []),
                    Search(wrongName, total: 1, ["item-wrong"]),
                    Search(ambiguous, total: 2, ["item-ambiguous-a", "item-ambiguous-b"]),
                ],
                itemsByLabel: new Dictionary<string, IReadOnlyList<PublicStashItem>>(StringComparer.Ordinal)
                {
                    [synchronized.Label] =
                    [
                        Item(synchronized, stashId: "stash-1"),
                    ],
                    [wrongName.Label] =
                    [
                        Item(wrongName, tabName: "~price another mirror", stashId: "stash-2"),
                    ],
                    [ambiguous.Label] =
                    [
                        Item(ambiguous, stashId: "stash-3"),
                        Item(ambiguous, stashId: "stash-4"),
                    ],
                },
                errorsByLabel: new Dictionary<string, Exception>(StringComparer.Ordinal)
                {
                    [failed.Label] = new TradeApiException("rate limited"),
                });
            var useCase = new PublicTabsSetupUseCase(
                gateway,
                new PublicStashSettingsStore(Path.Combine(directory, "public-stash.json")));

            var result = await useCase.SynchronizeAsync(new PublicTabsSetupRequest(
                " account#1234 ",
                " League ",
                [synchronized, notFound, wrongName, ambiguous, failed, excluded]));

            Assert.Equal("account#1234", result.AccountName);
            Assert.Equal("League", result.League);
            Assert.Equal(
                [
                    PublicTabSynchronizationStatus.Synchronized,
                    PublicTabSynchronizationStatus.NotFound,
                    PublicTabSynchronizationStatus.WrongTabName,
                    PublicTabSynchronizationStatus.Ambiguous,
                    PublicTabSynchronizationStatus.Error,
                    PublicTabSynchronizationStatus.Excluded,
                ],
                result.Tabs.Select(tab => tab.Status));
            Assert.True(result.Tabs[0].IsSynchronized);
            Assert.Contains("another", result.Tabs[2].RussianSummary, StringComparison.Ordinal);
            Assert.False(result.AreAllSelectedTabsSynchronized);
            Assert.Equal("Синхронизировано 1 из 5.", result.RussianSummary);
            Assert.Equal(
                [synchronized.Label, notFound.Label, wrongName.Label, ambiguous.Label, failed.Label],
                gateway.SearchLabels);
            Assert.DoesNotContain(excluded.Label, gateway.SearchLabels);
            Assert.Equal([synchronized.Label, wrongName.Label, ambiguous.Label], gateway.FetchLabels);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_persists_only_selected_successfully_synchronized_tabs()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new PublicStashSettingsStore(Path.Combine(directory, "public-stash.json"));
            var useCase = new PublicTabsSetupUseCase(new FakeTradeGateway(), store);
            var saved = new PublicTabsSetupTab(
                "Разлом",
                "~price 1001 mirror",
                1001m,
                "mirror",
                IsSelected: true);
            var failed = Tab("Failed", isSelected: true);
            var excluded = Tab("Excluded", isSelected: false);

            await useCase.SaveAsync(new PublicTabsSynchronizationResult(
                "account",
                "league",
                [
                    new PublicTabSynchronizationResult(saved, PublicTabSynchronizationStatus.Synchronized, "ok"),
                    new PublicTabSynchronizationResult(failed, PublicTabSynchronizationStatus.NotFound, "not found"),
                    new PublicTabSynchronizationResult(excluded, PublicTabSynchronizationStatus.Synchronized, "must remain excluded"),
                ]));

            var persisted = Assert.IsType<PublicStashSettings>(store.Get());
            var marker = Assert.Single(persisted.TabMarkers!);
            Assert.Equal(saved.Label, marker.Label);
            Assert.Equal(saved.TabName, marker.TabName);
            Assert.True(useCase.HasSavedConfiguration());

            var selections = useCase.GetTabs().ToDictionary(tab => tab.Label, StringComparer.Ordinal);
            Assert.True(selections[saved.Label].IsSelected);
            Assert.False(selections["Бездна"].IsSelected);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_rejects_a_result_without_successful_selected_tabs()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var useCase = new PublicTabsSetupUseCase(
                new FakeTradeGateway(),
                new PublicStashSettingsStore(Path.Combine(directory, "public-stash.json")));
            var tab = Tab("Not saved", isSelected: true);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.SaveAsync(
                new PublicTabsSynchronizationResult(
                    "account",
                    "league",
                    [new PublicTabSynchronizationResult(tab, PublicTabSynchronizationStatus.Error, "error")])));

            Assert.Contains("нет успешно", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PublicTabsSetupTab Tab(string label, bool isSelected)
    {
        var markerPrice = label.Sum(character => (decimal)character);
        return new PublicTabsSetupTab(
            label,
            $"~price {markerPrice:0.####} mirror",
            markerPrice,
            "mirror",
            isSelected);
    }

    private static PublicStashSearchResult Search(PublicTabsSetupTab tab, int total, IReadOnlyList<string> itemIds) => new(
        tab.Label,
        tab.PriceAmount,
        tab.PriceCurrency,
        total,
        $"query-{tab.Label}",
        itemIds);

    private static PublicStashItem Item(PublicTabsSetupTab tab, string? tabName = null, string? stashId = null) => new(
        $"item-{tab.Label}-{stashId}",
        tabName ?? tab.TabName,
        "Orb",
        1,
        0,
        0,
        tab.Label,
        stashId);

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"poe2-clock-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeTradeGateway : IPublicTabsSetupTradeGateway
    {
        private readonly IReadOnlyDictionary<string, PublicStashSearchResult> _searches;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PublicStashItem>> _itemsByLabel;
        private readonly IReadOnlyDictionary<string, Exception> _errorsByLabel;

        public FakeTradeGateway(
            IReadOnlyList<PublicStashSearchResult>? searches = null,
            IReadOnlyDictionary<string, IReadOnlyList<PublicStashItem>>? itemsByLabel = null,
            IReadOnlyDictionary<string, Exception>? errorsByLabel = null)
        {
            _searches = (searches ?? [])
                .ToDictionary(search => search.Label, StringComparer.Ordinal);
            _itemsByLabel = itemsByLabel ?? new Dictionary<string, IReadOnlyList<PublicStashItem>>(StringComparer.Ordinal);
            _errorsByLabel = errorsByLabel ?? new Dictionary<string, Exception>(StringComparer.Ordinal);
        }

        public List<string> SearchLabels { get; } = [];

        public List<string> FetchLabels { get; } = [];

        public Task<PublicStashSearchResult> SearchAsync(
            string accountName,
            string league,
            PublicStashTabMarker marker,
            CancellationToken cancellationToken = default)
        {
            SearchLabels.Add(marker.Label);
            cancellationToken.ThrowIfCancellationRequested();
            if (_errorsByLabel.TryGetValue(marker.Label, out var error))
            {
                throw error;
            }

            return Task.FromResult(_searches[marker.Label]);
        }

        public Task<IReadOnlyList<PublicStashItem>> FetchAsync(
            PublicStashSearchResult search,
            CancellationToken cancellationToken = default)
        {
            FetchLabels.Add(search.Label);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_itemsByLabel.TryGetValue(search.Label, out var items)
                ? items
                : (IReadOnlyList<PublicStashItem>)[]);
        }
    }
}
