using System.Text.Json;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.Persistence;

namespace Poe2DesktopClock.Infrastructure.Storage.Snapshots;

/// <summary>
/// Persists the latest successfully calculated clock snapshot for the next game session.
/// </summary>
public sealed class LastClockSnapshotStore : ILastClockSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private ClockSnapshot? _snapshot;
    private bool _loaded;

    public LastClockSnapshotStore(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ConfigurationPath = configurationPath;
    }

    public string ConfigurationPath { get; }

    public ClockSnapshot? GetLastSnapshot()
    {
        lock (_sync)
        {
            Load();
            return _snapshot;
        }
    }

    public void Save(ClockSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsValid(snapshot))
        {
            throw new ArgumentException("Снимок часов содержит некорректную оценку.", nameof(snapshot));
        }

        lock (_sync)
        {
            ResilientJsonFile.WriteAtomically(ConfigurationPath, snapshot, JsonOptions);
            _snapshot = snapshot;
            _loaded = true;
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _snapshot = ResilientJsonFile.ReadOrBackupCorrupted<ClockSnapshot>(
            ConfigurationPath,
            JsonOptions,
            IsValid);
        _loaded = true;
    }

    private static bool IsValid(ClockSnapshot snapshot) =>
        snapshot.TotalDivines >= 0m &&
        snapshot.CurrencyTabDivines >= 0m &&
        snapshot.PublicTabsDivines >= 0m &&
        snapshot.TotalDivines == snapshot.CurrencyTabDivines + snapshot.PublicTabsDivines &&
        !string.IsNullOrWhiteSpace(snapshot.RussianSummary);
}
