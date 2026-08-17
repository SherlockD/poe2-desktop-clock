using System.Text.Json;
using Poe2DesktopClock.Application.Models;
using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

/// <summary>Persists the last complete public-tab read for automatic incremental refreshes.</summary>
public sealed class PublicTabsSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private StoredPublicTabsSnapshot? _snapshot;
    private bool _loaded;

    public PublicTabsSnapshotStore(string configurationPath)
    {
        ConfigurationPath = configurationPath;
    }

    public string ConfigurationPath { get; }

    public StoredPublicTabsSnapshot? Get()
    {
        lock (_sync)
        {
            if (_loaded)
            {
                return _snapshot;
            }

            _loaded = true;
            if (!File.Exists(ConfigurationPath))
            {
                return null;
            }

            try
            {
                _snapshot = JsonSerializer.Deserialize<StoredPublicTabsSnapshot>(File.ReadAllText(ConfigurationPath), JsonOptions);
            }
            catch (JsonException)
            {
                _snapshot = null;
            }

            return _snapshot;
        }
    }

    public void Save(StoredPublicTabsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(ConfigurationPath)
                ?? throw new InvalidOperationException("Не удалось определить папку для снимка публичных вкладок.");
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{ConfigurationPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporaryPath, ConfigurationPath, overwrite: true);
            _snapshot = snapshot;
            _loaded = true;
        }
    }
}

public sealed record StoredPublicTabsSnapshot(
    string AccountName,
    string League,
    IReadOnlyList<StoredPublicTabMarkerSnapshot> Markers,
    DateTimeOffset LastFullFetchAt,
    string InventoryFingerprint,
    string? PriceFingerprint,
    PublicTabsValuation? Valuation);

public sealed record StoredPublicTabMarkerSnapshot(
    string Label,
    string TabName,
    decimal PriceAmount,
    string PriceCurrency,
    int TotalMatches,
    IReadOnlyList<string> ItemIds,
    IReadOnlyList<PublicStashItem> Items);
