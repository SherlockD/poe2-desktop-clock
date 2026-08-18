using System.Net;
using System.Text.Json;

namespace Poe2DeskTracker.Pricing;

/// <summary>
/// Loads the current PoE 2 economy snapshot from poe.ninja. Its primary value
/// is Divine Orb, so every matched item can be valued without a second currency
/// conversion step.
/// </summary>
public sealed class PoeNinjaPriceClient
{
    public const string HttpClientName = "PoeNinja";

    private static readonly string[] EconomyTypes =
    [
        "Currency",
        "Fragments",
        "Abyss",
        "Essences",
        "SoulCores",
        "Idols",
        "Runes",
        "Ritual",
        "Expedition",
        "Delirium",
        "Breach",
        "Verisium",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedPriceSnapshot> _snapshots = new(StringComparer.Ordinal);

    public PoeNinjaPriceClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PoeNinjaPriceSnapshot> GetPricesAsync(
        string league,
        TimeSpan? cacheDuration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(league);
        var cacheKey = league.Trim();
        lock (_sync)
        {
            var effectiveCacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
            if (_snapshots.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt < effectiveCacheDuration)
            {
                return cached.Snapshot;
            }
        }

        var overviews = new List<EconomyOverview>(EconomyTypes.Length);
        foreach (var economyType in EconomyTypes)
        {
            var relativeUri = $"poe2/api/economy/exchange/current/overview?league={Uri.EscapeDataString(cacheKey)}&type={Uri.EscapeDataString(economyType)}";
            using var response = await _httpClientFactory
                .CreateClient(HttpClientName)
                .GetAsync(relativeUri, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = payload.Length > 300 ? $"{payload[..300]}…" : payload;
                throw new PoeNinjaPriceException($"Price request for {economyType} failed ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
            }

            var overview = JsonSerializer.Deserialize<EconomyOverview>(payload, JsonOptions)
                ?? throw new PoeNinjaPriceException($"Price response for {economyType} could not be read.");
            if (!string.Equals(overview.Core?.Primary, "divine", StringComparison.OrdinalIgnoreCase))
            {
                throw new PoeNinjaPriceException($"Price response for {economyType} is not denominated in Divine Orbs.");
            }

            overviews.Add(overview);
        }

        var prices = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var overview in overviews)
        {
            var itemNamesById = (overview.Items ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .ToDictionary(item => item.Id!, item => item.Name!, StringComparer.Ordinal);
            foreach (var line in overview.Lines ?? [])
            {
                if (line.PrimaryValue is not > 0 || string.IsNullOrWhiteSpace(line.Id) ||
                    !itemNamesById.TryGetValue(line.Id, out var itemName))
                {
                    continue;
                }

                var key = NormalizeItemName(itemName);
                // A duplicate can occur in adjacent economic categories. Keep
                // the higher non-zero value rather than dropping a valid quote.
                prices[key] = Math.Max(prices.GetValueOrDefault(key), line.PrimaryValue.Value);
            }
        }

        var snapshot = new PoeNinjaPriceSnapshot(DateTimeOffset.UtcNow, prices);
        lock (_sync)
        {
            _snapshots[cacheKey] = new CachedPriceSnapshot(snapshot.RetrievedAt, snapshot);
        }

        return snapshot;
    }

    public static string NormalizeItemName(string itemName) =>
        string.Join(' ', itemName.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private sealed record EconomyOverview(EconomyCore? Core, List<EconomyItem>? Items, List<EconomyLine>? Lines);

    private sealed record EconomyCore(string? Primary);

    private sealed record EconomyItem(string? Id, string? Name);

    private sealed record EconomyLine(string? Id, decimal? PrimaryValue);

    private sealed record CachedPriceSnapshot(DateTimeOffset CreatedAt, PoeNinjaPriceSnapshot Snapshot);
}
