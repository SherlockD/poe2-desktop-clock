using System.Diagnostics;
using System.Globalization;
using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Game;
using Poe2DeskTracker.Interop;
using Poe2DeskTracker.Pricing;
using Poe2DeskTracker.PublicStash;
using Poe2DeskTracker.Regions;

var locator = new PoeProcessLocator();
using var capture = new WindowsGraphicsCaptureService();
var regionConfigurationPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Poe2DeskTracker",
    "regions.json");
var legacyRegionConfigurationPaths = new List<string>
{
    Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "config", "regions.json")),
    Path.Combine(AppContext.BaseDirectory, "config", "regions.json"),
};
var buildOutputDirectory = Path.Combine(Environment.CurrentDirectory, "bin");
if (Directory.Exists(buildOutputDirectory))
{
    legacyRegionConfigurationPaths.AddRange(Directory.EnumerateFiles(buildOutputDirectory, "regions.json", SearchOption.AllDirectories));
}

var regionStore = new RegionStore(regionConfigurationPath, legacyRegionConfigurationPaths.ToArray());
var configurationDirectory = Path.GetDirectoryName(regionConfigurationPath)!;
var currencyLayoutStore = new CurrencyLayoutStore(Path.Combine(configurationDirectory, "currency-layouts.json"));
var publicStashSettingsStore = new PublicStashSettingsStore(Path.Combine(configurationDirectory, "public-stash.json"));
using var tradeApiClient = new TradeApiClient();
using var poeNinjaPriceClient = new PoeNinjaPriceClient();

Console.WriteLine("PoE 2 Desk Tracker");
Console.WriteLine("Commands: status, debug-frame, currency, public, worth, help, exit");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim();
    var command = input?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

    switch (command)
    {
        case "status":
            PrintStatus(locator);
            break;

        case "debug-frame":
            await SaveDebugFrameAsync(locator, capture);
            break;

        case "currency":
            await HandleCurrencyCommandAsync(input, locator, capture, regionStore, currencyLayoutStore);
            break;

        case "public":
            await HandlePublicStashCommandAsync(input, publicStashSettingsStore, tradeApiClient, poeNinjaPriceClient);
            break;

        case "worth":
            await HandleWorthCommandAsync(
                input,
                locator,
                capture,
                regionStore,
                currencyLayoutStore,
                publicStashSettingsStore,
                tradeApiClient,
                poeNinjaPriceClient);
            break;

        case "help":
            Console.WriteLine("status       Show whether a usable Path of Exile 2 window is available.");
            Console.WriteLine("debug-frame  Capture one frame from the current PoE 2 window to debug/frame.png.");
            Console.WriteLine("currency setup      Select the one currency-tab region.");
            Console.WriteLine("currency calibrate  Detect and review its 33 dedicated slots.");
            Console.WriteLine("currency scan       Read quantities from the saved layout.");
            Console.WriteLine("currency reset      Delete the currency region and layout, then start over.");
            Console.WriteLine("public setup        Configure public tab names and unique marker prices manually.");
            Console.WriteLine("public scan         Read all listed items from each configured public tab through Trade API.");
            Console.WriteLine("public list         Show the saved public-tab configuration.");
            Console.WriteLine("public add [label]  Add another public tab marker interactively.");
            Console.WriteLine("public remove <n|label|name>  Stop tracking a public tab.");
            Console.WriteLine("public reset        Forget the public-tab configuration.");
            Console.WriteLine("worth scan          Estimate the combined value of the currency tab and public tabs.");
            Console.WriteLine("exit         Quit the tracker.");
            break;

        case "exit":
        case "quit":
            return;

        case null:
            return;

        case "":
            break;

        default:
            Console.WriteLine("Unknown command. Type help.");
            break;
    }
}

static async Task HandlePublicStashCommandAsync(
    string? input,
    PublicStashSettingsStore settingsStore,
    TradeApiClient tradeApiClient,
    PoeNinjaPriceClient poeNinjaPriceClient)
{
    var parts = input?.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries) ?? [];
    if (parts.Length < 2)
    {
        PrintPublicStashUsage();
        return;
    }

    var operation = parts[1].ToLowerInvariant();
    var argument = parts.Length == 3 ? parts[2].Trim() : null;
    try
    {
        switch (operation)
        {
            case "setup":
                await SetupPublicStashAsync(settingsStore, tradeApiClient);
                break;

            case "scan":
                await ScanPublicStashesAsync(settingsStore, tradeApiClient, poeNinjaPriceClient);
                break;

            case "list":
                PrintPublicStashSettings(settingsStore.Get(), settingsStore.ConfigurationPath);
                break;

            case "add":
                AddPublicStashTab(settingsStore, argument);
                break;

            case "remove":
                RemovePublicStashTab(settingsStore, argument);
                break;

            case "reset":
                settingsStore.Clear();
                Console.WriteLine("Public stash setup was reset. Run: public setup");
                break;

            default:
                PrintPublicStashUsage();
                break;
        }
    }
    catch (TradeApiException exception)
    {
        Console.WriteLine($"Public stash request failed: {exception.Message}");
    }
    catch (PoeNinjaPriceException exception)
    {
        Console.WriteLine($"Price request failed: {exception.Message}");
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Public stash request failed: {exception.Message}");
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine("Public stash request timed out. Check the connection and try again.");
    }
    catch (ArgumentException exception)
    {
        Console.WriteLine($"Public stash setup is invalid: {exception.Message}");
    }
}

static async Task SetupPublicStashAsync(
    PublicStashSettingsStore settingsStore,
    TradeApiClient tradeApiClient)
{
    var existing = settingsStore.Get();
    var accountName = PromptRequiredValue("Account name", GetSafeSetupDefault(existing?.AccountName));
    if (accountName is null)
    {
        Console.WriteLine("Public stash setup cancelled.");
        return;
    }

    var defaultLeague = GetSafeSetupDefault(existing?.League);
    if (string.IsNullOrWhiteSpace(defaultLeague))
    {
        Console.WriteLine("Looking up current PoE 2 leagues...");
        var leagues = await tradeApiClient.GetPoe2LeagueNamesAsync();
        defaultLeague = leagues.FirstOrDefault(league =>
                            !league.StartsWith("HC", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(league, "Standard", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(league, "Hardcore", StringComparison.OrdinalIgnoreCase))
                        ?? leagues.FirstOrDefault();
    }

    var league = PromptRequiredValue("League", defaultLeague);
    if (league is null)
    {
        Console.WriteLine("Public stash setup cancelled.");
        return;
    }

    Console.WriteLine("Set each in-game tab Public and use the following unique deliberately-high default prices:");
    var suggestedLabels = GetSuggestedPublicTabLabels();
    for (var index = 0; index < suggestedLabels.Count; index++)
    {
        var label = suggestedLabels[index];
        Console.WriteLine($"  {label}: {FormatPublicTabName(1001m + index, "mirror")}");
    }

    Console.WriteLine("Every item in one tracked tab must inherit that tab's default price. Do not set individual item prices.");
    Console.Write("Press Enter after renaming the tabs to save this configuration, or type cancel: ");
    var confirmation = Console.ReadLine();
    if (confirmation is null || string.Equals(confirmation.Trim(), "cancel", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Public stash setup cancelled.");
        return;
    }

    var markers = suggestedLabels
        .Select((label, index) => new PublicStashTabMarker(label, FormatPublicTabName(1001m + index, "mirror"), 1001m + index, "mirror"))
        .ToList();

    settingsStore.Save(new PublicStashSettings(accountName, league, [], markers));
    Console.WriteLine($"Saved {markers.Count} public tab marker(s) to {settingsStore.ConfigurationPath}");
}

static async Task<InventoryValuePart?> ScanPublicStashesAsync(
    PublicStashSettingsStore settingsStore,
    TradeApiClient tradeApiClient,
    PoeNinjaPriceClient poeNinjaPriceClient,
    PoeNinjaPriceSnapshot? prices = null,
    bool loadPricesWhenMissing = true)
{
    var settings = settingsStore.Get();
    if (settings is null)
    {
        Console.WriteLine("Public stash is not set up. Run: public setup");
        return null;
    }

    if (!settings.HasCompleteMarkers)
    {
        Console.WriteLine("Public stash uses the old name-only setup. Run: public setup to add unique marker prices.");
        return null;
    }

    Console.WriteLine($"Scanning public tabs for {settings.AccountName} in {settings.League}...");
    var tabMarkers = settings.TabMarkers!;
    var discovery = await tradeApiClient.DiscoverPublicTabItemsAsync(
        settings.AccountName,
        settings.League,
        tabMarkers,
        CreatePublicStashProgress(),
        CreatePublicStashFetchProgress());
    PrintTruncationWarning(discovery);

    var configuredTabs = new HashSet<string>(settings.TabNames, StringComparer.Ordinal);
    var selectedItems = discovery.Items
        .Where(item => configuredTabs.Contains(item.TabName))
        .GroupBy(item => item.Id ?? $"{item.TabName}\u001f{item.X}\u001f{item.Y}\u001f{item.ItemName}", StringComparer.Ordinal)
        .Select(group => group.First())
        .ToArray();

    Console.WriteLine($"Public tabs: {settings.TabNames.Count}; marker queries: {discovery.SearchGroups.Count}; matched stacks: {selectedItems.Length}; total items: {selectedItems.Sum(item => item.StackSize)}");
    if (selectedItems.Length == 0)
    {
        PrintPublicScanCompletenessWarning(settings, discovery, selectedItems);
        return new InventoryValuePart(0m, 0, 0, IsComplete: false);
    }

    if (prices is null && loadPricesWhenMissing)
    {
        try
        {
            Console.WriteLine("Loading current Divine prices...");
            prices = await poeNinjaPriceClient.GetPricesAsync(settings.League);
            Console.WriteLine($"Price snapshot: {prices.RetrievedAt.ToLocalTime():g}");
        }
        catch (PoeNinjaPriceException exception)
        {
            Console.WriteLine($"Price snapshot is unavailable. Quantities will be shown without values: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"Price snapshot is unavailable. Quantities will be shown without values: {exception.Message}");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Price snapshot is unavailable. The price request timed out.");
        }
    }

    var totalDivines = 0m;
    var unpricedItemTypes = 0;
    foreach (var tabName in settings.TabNames)
    {
        var tabItems = selectedItems
            .Where(item => string.Equals(item.TabName, tabName, StringComparison.Ordinal))
            .GroupBy(item => item.ItemName, StringComparer.Ordinal)
            .Select(group => CreatePricedPublicStashQuantity(group.Key, group.Sum(item => item.StackSize), prices))
            .OrderByDescending(item => item.TotalDivines ?? decimal.MinValue)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine();
        Console.WriteLine(tabName);
        if (tabItems.Length == 0)
        {
            Console.WriteLine("  No entries for this marker price. The tab may be empty, private, not indexed yet, or configured with a different price/name.");
            continue;
        }

        var tabTotalDivines = 0m;
        foreach (var item in tabItems)
        {
            if (item.TotalDivines is { } total && item.UnitDivines is { } unit)
            {
                tabTotalDivines += total;
                totalDivines += total;
                Console.WriteLine($"  {item.Name}: {item.Amount} × {FormatDivines(unit)} div = {FormatDivines(total)} div");
            }
            else
            {
                unpricedItemTypes++;
                Console.WriteLine($"  {item.Name}: {item.Amount} × ? div");
            }
        }

        Console.WriteLine($"  Estimated tab value: {FormatDivines(tabTotalDivines)} div");
    }

    Console.WriteLine();
    var isComplete = PrintPublicScanCompletenessWarning(settings, discovery, selectedItems);
    Console.WriteLine(prices is null
        ? "Estimated public-stash value: unavailable"
        : isComplete
            ? $"Estimated public-stash value: {FormatDivines(totalDivines)} div"
            : $"Observed partial public-stash value: {FormatDivines(totalDivines)} div");
    if (unpricedItemTypes > 0)
    {
        Console.WriteLine($"No current price was found for {unpricedItemTypes} item type(s); they are excluded from the estimate.");
    }

    return new InventoryValuePart(totalDivines, unpricedItemTypes, 0, isComplete);
}

static async Task HandleWorthCommandAsync(
    string? input,
    PoeProcessLocator locator,
    WindowsGraphicsCaptureService capture,
    RegionStore regionStore,
    CurrencyLayoutStore currencyLayoutStore,
    PublicStashSettingsStore publicStashSettingsStore,
    TradeApiClient tradeApiClient,
    PoeNinjaPriceClient poeNinjaPriceClient)
{
    var parts = input?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries) ?? [];
    if (parts.Length != 2 || !string.Equals(parts[1], "scan", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Usage: worth scan");
        return;
    }

    var publicSettings = publicStashSettingsStore.Get();
    if (publicSettings is null || !publicSettings.HasCompleteMarkers)
    {
        Console.WriteLine("Public stash marker prices are not set up. Run: public setup, then worth scan");
        return;
    }

    Console.WriteLine($"Loading current Divine prices for {publicSettings.League}...");
    var priceTask = poeNinjaPriceClient.GetPricesAsync(publicSettings.League);
    var currencyScan = await ScanCurrencyAmountsAsync(locator, capture, regionStore, currencyLayoutStore);

    PoeNinjaPriceSnapshot? prices = null;
    try
    {
        prices = await priceTask;
        Console.WriteLine($"Price snapshot: {prices.RetrievedAt.ToLocalTime():g}");
    }
    catch (PoeNinjaPriceException exception)
    {
        Console.WriteLine($"Price snapshot is unavailable. Quantities will be shown without values: {exception.Message}");
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Price snapshot is unavailable. Quantities will be shown without values: {exception.Message}");
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine("Price snapshot is unavailable. The price request timed out.");
    }

    InventoryValuePart? currencyValue = null;
    if (currencyScan is not null)
    {
        currencyValue = PrintCurrencyValue(currencyScan, prices);
    }

    InventoryValuePart? publicValue;
    try
    {
        publicValue = await ScanPublicStashesAsync(
            publicStashSettingsStore,
            tradeApiClient,
            poeNinjaPriceClient,
            prices,
            loadPricesWhenMissing: false);
    }
    catch (TradeApiException exception)
    {
        Console.WriteLine($"Public stash request failed: {exception.Message}");
        publicValue = null;
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Public stash request failed: {exception.Message}");
        publicValue = null;
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine("Public stash request timed out. Check the connection and try again.");
        publicValue = null;
    }

    if (currencyValue is null || publicValue is null)
    {
        Console.WriteLine("Combined estimate is incomplete because one source could not be scanned.");
        return;
    }

    if (prices is null)
    {
        Console.WriteLine("Combined estimate is unavailable because the price snapshot could not be loaded.");
        return;
    }

    if (!publicValue.IsComplete)
    {
        Console.WriteLine("Combined estimate is unavailable because the public-tab scan is incomplete. Resolve its warnings and scan again.");
        return;
    }

    var totalDivines = currencyValue.TotalDivines + publicValue.TotalDivines;
    Console.WriteLine();
    Console.WriteLine($"Estimated total inventory value: {FormatDivines(totalDivines)} div");

    var unpricedItemTypes = currencyValue.UnpricedItemTypes + publicValue.UnpricedItemTypes;
    var unreadableCurrencySlots = currencyValue.UnreadableItemTypes;
    if (unpricedItemTypes > 0 || unreadableCurrencySlots > 0)
    {
        Console.WriteLine($"Excluded from total: {unpricedItemTypes} unpriced item type(s), {unreadableCurrencySlots} unreadable currency slot(s).");
    }
}

static void AddPublicStashTab(PublicStashSettingsStore settingsStore, string? argument)
{
    var settings = settingsStore.Get();
    if (settings is null || !settings.HasCompleteMarkers)
    {
        Console.WriteLine("Public stash marker prices are not set up. Run: public setup");
        return;
    }

    var label = NormalizeTabNameArgument(argument) ?? PromptRequiredValue("Category label", null);
    if (label is null)
    {
        Console.WriteLine("Public tab addition cancelled.");
        return;
    }

    var tabName = PromptRequiredValue($"{label} — public tab name", null);
    if (tabName is null)
    {
        Console.WriteLine("Public tab addition cancelled.");
        return;
    }

    var nextPrice = settings.TabMarkers!.Max(marker => marker.PriceAmount) + 1m;
    var priceAmount = PromptPositiveDecimal($"{label} — marker price", nextPrice);
    var priceCurrency = PromptRequiredValue($"{label} — marker currency", "mirror");
    if (priceAmount is null || priceCurrency is null)
    {
        Console.WriteLine("Public tab addition cancelled.");
        return;
    }

    var markers = new List<PublicStashTabMarker>(settings.TabMarkers!)
    {
        new(label, tabName, priceAmount.Value, priceCurrency),
    };
    settingsStore.Save(settings with { TabNames = [], TabMarkers = markers });
    Console.WriteLine($"Added public tab marker: {label} — {tabName} — {FormatMarkerPrice(priceAmount.Value, priceCurrency)}");
}

static void RemovePublicStashTab(PublicStashSettingsStore settingsStore, string? argument)
{
    var settings = settingsStore.Get();
    if (settings is null || !settings.HasCompleteMarkers)
    {
        Console.WriteLine("Public stash marker prices are not set up. Run: public setup");
        return;
    }

    if (string.IsNullOrWhiteSpace(argument))
    {
        Console.WriteLine("Usage: public remove <number|exact tab name>");
        return;
    }

    var markers = new List<PublicStashTabMarker>(settings.TabMarkers!);
    var normalizedArgument = NormalizeTabNameArgument(argument);
    var removeIndex = int.TryParse(argument, out var number) && number >= 1 && number <= markers.Count
        ? number - 1
        : markers.FindIndex(marker =>
            string.Equals(marker.Label, normalizedArgument, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(marker.TabName, normalizedArgument, StringComparison.Ordinal));
    if (removeIndex < 0)
    {
        Console.WriteLine("Tracked public tab was not found. Run: public list");
        return;
    }

    if (markers.Count == 1)
    {
        Console.WriteLine("Use 'public reset' to remove the final tracked tab.");
        return;
    }

    var removedTab = markers[removeIndex];
    markers.RemoveAt(removeIndex);
    settingsStore.Save(settings with { TabNames = [], TabMarkers = markers });
    Console.WriteLine($"Stopped tracking public tab: {removedTab.Label} ({removedTab.TabName})");
}

static void PrintPublicStashSettings(PublicStashSettings? settings, string configurationPath)
{
    if (settings is null)
    {
        Console.WriteLine("Public stash is not set up. Run: public setup");
        return;
    }

    Console.WriteLine($"Account: {settings.AccountName}");
    Console.WriteLine($"League: {settings.League}");
    if (!settings.HasCompleteMarkers)
    {
        Console.WriteLine("This is an old name-only configuration. Run: public setup");
        if (settings.TabNames.Count > 0)
        {
            Console.WriteLine("Saved legacy tab names:");
            foreach (var tabName in settings.TabNames)
            {
                Console.WriteLine($"  {tabName}");
            }
        }

        return;
    }

    Console.WriteLine("Tracked public tabs:");
    for (var index = 0; index < settings.TabMarkers!.Count; index++)
    {
        var marker = settings.TabMarkers[index];
        Console.WriteLine($"  {index + 1}. {marker.Label}: {marker.TabName} — {FormatMarkerPrice(marker.PriceAmount, marker.PriceCurrency)}");
    }

    Console.WriteLine($"Configuration: {configurationPath}");
}

static void PrintPublicStashUsage()
{
    Console.WriteLine("Usage: public setup | public scan | public list | public add [label] | public remove <number|label|name> | public reset");
}

static void PrintTruncationWarning(PublicStashDiscovery discovery)
{
    if (!discovery.IsTruncated)
    {
        return;
    }

    foreach (var group in discovery.SearchGroups.Where(group => group.IsTruncated))
    {
        Console.WriteLine($"WARNING: {group.Label} marker {FormatMarkerPrice(group.PriceAmount, group.PriceCurrency)} returned only {group.ReturnedItemIds} of {group.TotalMatches} listings. This tab scan is incomplete; do not treat its quantities as exact.");
    }
}

static IProgress<PublicStashSearchProgress> CreatePublicStashProgress() =>
    new Progress<PublicStashSearchProgress>(progress =>
        Console.WriteLine($"  Tab {progress.CompletedGroups}/{progress.TotalGroups}: {progress.Label} — {FormatMarkerPrice(progress.PriceAmount, progress.PriceCurrency)} — {progress.ReturnedItemIds} listings"));

static IProgress<PublicStashFetchProgress> CreatePublicStashFetchProgress() =>
    new Progress<PublicStashFetchProgress>(progress =>
        Console.WriteLine($"  Loading item details: batch {progress.CurrentBatch}/{progress.TotalBatches} ({progress.ItemCount} listings)"));

static bool PrintPublicScanCompletenessWarning(
    PublicStashSettings settings,
    PublicStashDiscovery discovery,
    IReadOnlyList<PublicStashItem> selectedItems)
{
    var isComplete = !discovery.IsTruncated;
    foreach (var marker in settings.TabMarkers!)
    {
        var markerItems = discovery.Items
            .Where(item => string.Equals(item.MarkerLabel, marker.Label, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var unexpectedTabs = markerItems
            .Where(item => !string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal))
            .Select(item => item.TabName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unexpectedTabs.Length > 0)
        {
            Console.WriteLine($"WARNING: {marker.Label} marker {FormatMarkerPrice(marker.PriceAmount, marker.PriceCurrency)} also returned another tab: {string.Join(", ", unexpectedTabs)}. Marker prices must be unique; this scan is not exact.");
            isComplete = false;
        }

        if (selectedItems.Any(item => string.Equals(item.TabName, marker.TabName, StringComparison.Ordinal)))
        {
            continue;
        }

        var markerSearch = discovery.SearchGroups.Single(group => string.Equals(group.Label, marker.Label, StringComparison.OrdinalIgnoreCase));
        var reason = markerSearch.TotalMatches == 0
            ? "the marker query returned no public listings"
            : $"the marker query returned {markerSearch.ReturnedItemIds} listing(s), but none belonged to the configured tab name";
        Console.WriteLine($"WARNING: {marker.Label} ({marker.TabName}) was not verified: {reason}. It may be empty, private, waiting for Trade indexing, or have a different name/price.");
        isComplete = false;
    }

    return isComplete;
}

static string FormatMarkerPrice(decimal amount, string currency)
{
    var price = amount.ToString("0.####", CultureInfo.InvariantCulture);
    return string.IsNullOrWhiteSpace(currency) ? price : $"{price} {currency}";
}

static string FormatPublicTabName(decimal amount, string currency) =>
    $"~price {FormatMarkerPrice(amount, currency)}";

static string? GetSafeSetupDefault(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var looksLikeSetupTranscript =
        value.Contains("~price", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("must inherit", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("do not set individual", StringComparison.OrdinalIgnoreCase);
    return looksLikeSetupTranscript ? null : value;
}

static string? PromptRequiredValue(string label, string? defaultValue)
{
    var suffix = string.IsNullOrWhiteSpace(defaultValue) ? string.Empty : $" [{defaultValue}]";
    Console.Write($"{label}{suffix}: ");
    var value = Console.ReadLine();
    if (value is null)
    {
        return null;
    }

    var trimmed = value.Trim();
    return trimmed.Length > 0 ? trimmed : defaultValue?.Trim();
}

static decimal? PromptPositiveDecimal(string label, decimal defaultValue)
{
    Console.Write($"{label} [{FormatMarkerPrice(defaultValue, string.Empty).Trim()}]: ");
    var value = Console.ReadLine();
    if (value is null)
    {
        return null;
    }

    var trimmed = value.Trim();
    if (trimmed.Length == 0)
    {
        return defaultValue;
    }

    return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentCultureValue) && currentCultureValue > 0
        ? currentCultureValue
        : decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue) && invariantValue > 0
            ? invariantValue
            : null;
}

static IReadOnlyList<string> GetSuggestedPublicTabLabels() =>
[
    "Разлом",
    "Бездна",
    "Ритуал",
    "Экспедиция",
    "Делириум",
    "Сущности",
    "Руны",
    "Фрагменты",
];

static string? NormalizeTabNameArgument(string? argument)
{
    if (string.IsNullOrWhiteSpace(argument))
    {
        return null;
    }

    var trimmed = argument.Trim();
    return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
        ? trimmed[1..^1].Trim()
        : trimmed;
}

static PricedPublicStashQuantity CreatePricedPublicStashQuantity(
    string name,
    long amount,
    PoeNinjaPriceSnapshot? prices)
{
    if (prices is null || !prices.TryGetDivinePrice(name, out var unitDivines))
    {
        return new PricedPublicStashQuantity(name, amount, null, null);
    }

    return new PricedPublicStashQuantity(name, amount, unitDivines, unitDivines * amount);
}

static string FormatDivines(decimal value) =>
    value.ToString(value >= 10 ? "0.##" : "0.######", CultureInfo.InvariantCulture);

static async Task SetupCurrencyRegionAsync(
    PoeProcessLocator locator,
    WindowsGraphicsCaptureService capture,
    RegionStore regionStore,
    CurrencyLayoutStore currencyLayoutStore)
{
    var gameWindow = locator.FindGameWindow(includeMinimized: true);
    if (gameWindow is null)
    {
        Console.WriteLine("PoE 2: NOT FOUND — start the game and try again.");
        return;
    }

    Console.WriteLine("Restoring and activating PoE 2...");
    Win32Native.RestoreAndActivateWindow(gameWindow.Handle);
    if (!await WaitForStableClientBoundsAsync(gameWindow.Handle, TimeSpan.FromSeconds(2)))
    {
        Console.WriteLine("PoE 2 window did not become ready for selection. Ensure it is visible and try again.");
        return;
    }

    Console.WriteLine("Opening selector. Drag to select; Enter saves; Esc cancels.");
    RegionDefinition? region;
    try
    {
        region = await RegionSelectionOverlay.SelectAsync(gameWindow.Handle, "currency");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Region selector failed: {exception.Message}");
        return;
    }

    if (region is null)
    {
        Console.WriteLine("Region selection cancelled.");
        return;
    }

    regionStore.Clear();
    currencyLayoutStore.Clear();
    regionStore.Upsert(region);
    Console.WriteLine($"Saved the currency region to {regionStore.ConfigurationPath}. Calibrate it next: currency calibrate");
}

static async Task<CurrencyScreenScan?> ScanCurrencyAmountsAsync(
    PoeProcessLocator locator,
    WindowsGraphicsCaptureService capture,
    RegionStore regionStore,
    CurrencyLayoutStore currencyLayoutStore)
{
    var region = regionStore.GetAll().FirstOrDefault(savedRegion => string.Equals(savedRegion.Name, "currency", StringComparison.OrdinalIgnoreCase));
    if (region is null)
    {
        Console.WriteLine("Currency region is not set. Run: currency setup");
        return null;
    }

    var scanLayout = currencyLayoutStore.Get(region.Name);
    if (scanLayout is null || scanLayout.Slots.Count == 0)
    {
        Console.WriteLine("Currency slots are not calibrated. Run: currency calibrate");
        return null;
    }

    var gameWindow = locator.FindGameWindow();
    if (gameWindow is null)
    {
        Console.WriteLine("PoE 2: NOT FOUND — restore the game window and try again.");
        return null;
    }

    var previewPath = GetRegionPreviewPath(region.Name);
    try
    {
        Console.WriteLine($"Capturing '{region.Name}' for currency processing...");
        await capture.SaveRegionAsync(gameWindow.Handle, region, previewPath, TimeSpan.FromSeconds(5));
        var amounts = await CurrencyAmountScanner.ScanAsync(previewPath, scanLayout);
        var debugPath = GetCurrencyAmountDebugPath(region.Name);
        CurrencyAmountScanner.SaveDebugPreview(previewPath, debugPath, amounts);
        return new CurrencyScreenScan(amounts, debugPath);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Currency scan timed out. Ensure PoE 2 is visible and not minimized.");
        return null;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Currency scan failed: {exception.Message}");
        return null;
    }
}

static void PrintCurrencyQuantities(CurrencyScreenScan scan)
{
    foreach (var amount in scan.Amounts)
    {
        var value = amount.Amount?.ToString() ?? "?";
        Console.WriteLine($"{amount.Name}: {value}");
    }

    Console.WriteLine($"Quantity debug preview: {scan.DebugPath}");
}

static InventoryValuePart PrintCurrencyValue(CurrencyScreenScan scan, PoeNinjaPriceSnapshot? prices)
{
    Console.WriteLine();
    Console.WriteLine("Dedicated currency tab");

    var totalDivines = 0m;
    var unpricedItemTypes = 0;
    var unreadableSlots = 0;
    foreach (var amount in scan.Amounts.Where(amount => amount.Amount is null || amount.Amount > 0))
    {
        if (amount.Amount is null)
        {
            unreadableSlots++;
            Console.WriteLine($"  {amount.Name}: ?");
            continue;
        }

        if (!CurrencyTabProfile.TryGetPoeNinjaName(amount.Name, out var priceName) ||
            prices is null ||
            !prices.TryGetDivinePrice(priceName, out var unitDivines))
        {
            unpricedItemTypes++;
            Console.WriteLine($"  {amount.Name}: {amount.Amount} × ? div");
            continue;
        }

        var itemTotalDivines = unitDivines * amount.Amount.Value;
        totalDivines += itemTotalDivines;
        Console.WriteLine($"  {amount.Name}: {amount.Amount} × {FormatDivines(unitDivines)} div = {FormatDivines(itemTotalDivines)} div");
    }

    Console.WriteLine(prices is null
        ? "  Estimated currency-tab value: unavailable"
        : $"  Estimated currency-tab value: {FormatDivines(totalDivines)} div");
    Console.WriteLine($"  Quantity debug preview: {scan.DebugPath}");
    return new InventoryValuePart(totalDivines, unpricedItemTypes, unreadableSlots);
}

static async Task HandleCurrencyCommandAsync(
    string? input,
    PoeProcessLocator locator,
    WindowsGraphicsCaptureService capture,
    RegionStore regionStore,
    CurrencyLayoutStore currencyLayoutStore)
{
    var parts = input?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries) ?? [];
    if (parts.Length != 2)
    {
        Console.WriteLine("Usage: currency setup  |  currency calibrate  |  currency scan  |  currency reset");
        return;
    }

    var operation = parts[1].ToLowerInvariant();
    if (operation == "setup")
    {
        await SetupCurrencyRegionAsync(locator, capture, regionStore, currencyLayoutStore);
        return;
    }

    if (operation == "reset")
    {
        regionStore.Clear();
        currencyLayoutStore.Clear();
        Console.WriteLine("Currency setup was reset. Start again with: currency setup");
        return;
    }

    if (operation is not ("calibrate" or "scan"))
    {
        Console.WriteLine("Usage: currency setup  |  currency calibrate  |  currency scan  |  currency reset");
        return;
    }

    if (operation == "scan")
    {
        var scan = await ScanCurrencyAmountsAsync(locator, capture, regionStore, currencyLayoutStore);
        if (scan is not null)
        {
            PrintCurrencyQuantities(scan);
        }

        return;
    }

    var region = regionStore.GetAll().FirstOrDefault(savedRegion => string.Equals(savedRegion.Name, "currency", StringComparison.OrdinalIgnoreCase));
    if (region is null)
    {
        Console.WriteLine("Currency region is not set. Run: currency setup");
        return;
    }

    var gameWindow = locator.FindGameWindow();
    if (gameWindow is null)
    {
        Console.WriteLine("PoE 2: NOT FOUND — restore the game window and try again.");
        return;
    }

    var previewPath = GetRegionPreviewPath(region.Name);
    try
    {
        Console.WriteLine($"Capturing '{region.Name}' for currency processing...");
        await capture.SaveRegionAsync(gameWindow.Handle, region, previewPath, TimeSpan.FromSeconds(5));
        var detectedSlots = CurrencyTabProfile.Apply(CurrencyGridDetector.Detect(previewPath));
        if (detectedSlots.Count == 0)
        {
            Console.WriteLine("No candidate slot frames were detected.");
            return;
        }

        Console.WriteLine($"Detected {detectedSlots.Count} candidate slots. Review them in the calibration window.");
        var layout = await CurrencyCalibrationForm.CalibrateAsync(
            previewPath,
            region.Name,
            detectedSlots,
            currencyLayoutStore.Get(region.Name));
        if (layout is null)
        {
            Console.WriteLine("Currency calibration cancelled.");
            return;
        }

        currencyLayoutStore.Upsert(layout);
        Console.WriteLine($"Saved {layout.Slots.Count} slot definitions to {currencyLayoutStore.ConfigurationPath}");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Currency calibration timed out. Ensure PoE 2 is visible and not minimized.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Currency calibration failed: {exception.Message}");
    }
}

static string GetRegionPreviewPath(string regionName)
{
    var safeFileName = string.Concat(regionName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "debug", "regions", $"{safeFileName}.png"));
}

static string GetCurrencyAmountDebugPath(string regionName)
{
    var safeFileName = string.Concat(regionName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "debug", "regions", $"{safeFileName}-amounts.png"));
}

static async Task<bool> WaitForStableClientBoundsAsync(nint windowHandle, TimeSpan timeout)
{
    var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
    while (Stopwatch.GetTimestamp() < deadline)
    {
        if (!Win32Native.IsIconic(windowHandle) &&
            Win32Native.TryGetClientBoundsOnScreen(windowHandle, out var left, out var top, out var width, out var height))
        {
            await Task.Delay(150);
            if (!Win32Native.IsIconic(windowHandle) &&
                Win32Native.TryGetClientBoundsOnScreen(windowHandle, out var confirmedLeft, out var confirmedTop, out var confirmedWidth, out var confirmedHeight) &&
                left == confirmedLeft && top == confirmedTop && width == confirmedWidth && height == confirmedHeight)
            {
                return true;
            }
        }
        else
        {
            await Task.Delay(100);
        }
    }

    return false;
}

static void PrintStatus(PoeProcessLocator locator)
{
    var gameWindow = locator.FindGameWindow();
    if (gameWindow is null)
    {
        Console.WriteLine("PoE 2: NOT FOUND");
        Console.WriteLine("Waiting...");
        return;
    }

    Console.WriteLine($"PoE 2: FOUND (PID {gameWindow.ProcessId}, {gameWindow.Title})");
    Console.WriteLine($"Window: {gameWindow.Width}x{gameWindow.Height}");
    Console.WriteLine("Capture: READY");
}

static async Task SaveDebugFrameAsync(PoeProcessLocator locator, WindowsGraphicsCaptureService capture)
{
    var gameWindow = locator.FindGameWindow();
    if (gameWindow is null)
    {
        Console.WriteLine("PoE 2: NOT FOUND — start the game and try again.");
        return;
    }

    var outputPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "debug", "frame.png"));

    try
    {
        Console.WriteLine($"Capturing PID {gameWindow.ProcessId}...");
        var result = await capture.SaveSingleFrameAsync(gameWindow.Handle, outputPath, TimeSpan.FromSeconds(5));
        Console.WriteLine($"Capture: ACTIVE ({result.Width}x{result.Height}, {result.Elapsed.TotalMilliseconds:F0} ms)");
        Console.WriteLine($"Debug frame: {outputPath}");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Capture timed out. Ensure PoE 2 is visible and not minimized.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Capture failed: {exception.Message}");
    }
}

file sealed record PricedPublicStashQuantity(
    string Name,
    long Amount,
    decimal? UnitDivines,
    decimal? TotalDivines);

file sealed record CurrencyScreenScan(
    IReadOnlyList<CurrencyAmountScanResult> Amounts,
    string DebugPath);

file sealed record InventoryValuePart(
    decimal TotalDivines,
    int UnpricedItemTypes,
    int UnreadableItemTypes,
    bool IsComplete = true);
