using System.Text.Json;
using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Infrastructure.Storage.Persistence;
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
            _snapshot = ResilientJsonFile.ReadOrBackupCorrupted<StoredPublicTabsSnapshot>(
                ConfigurationPath,
                JsonOptions,
                IsValid);

            return _snapshot;
        }
    }

    public void Save(StoredPublicTabsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            ResilientJsonFile.WriteAtomically(ConfigurationPath, snapshot, JsonOptions);
            _snapshot = snapshot;
            _loaded = true;
        }
    }

    private static bool IsValid(StoredPublicTabsSnapshot snapshot) =>
        snapshot.AccountName is not null &&
        snapshot.League is not null &&
        snapshot.InventoryFingerprint is not null &&
        snapshot.Markers is not null &&
        snapshot.Markers.All(marker =>
            marker is not null &&
            marker.Label is not null &&
            marker.TabName is not null &&
            marker.PriceCurrency is not null &&
            marker.ItemIds is not null &&
            marker.Items is not null &&
            marker.Items.All(item =>
                item is not null &&
                item.TabName is not null &&
                item.ItemName is not null &&
                item.MarkerLabel is not null));
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
