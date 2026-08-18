using System.Text.Json;
using Poe2DesktopClock.Infrastructure.Windows.Persistence;

namespace Poe2DeskTracker.Currency;

public sealed class CurrencyLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private Dictionary<string, CurrencyLayout>? _layouts;

    public CurrencyLayoutStore(string configurationPath)
    {
        ConfigurationPath = configurationPath;
    }

    public string ConfigurationPath { get; }

    public CurrencyLayout? Get(string regionName)
    {
        lock (_sync)
        {
            return Load().GetValueOrDefault(regionName);
        }
    }

    public void Upsert(CurrencyLayout layout)
    {
        lock (_sync)
        {
            var layouts = Load();
            layouts[layout.RegionName] = layout;
            Save(layouts);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _layouts = new Dictionary<string, CurrencyLayout>(StringComparer.OrdinalIgnoreCase);
            Save(_layouts);
        }
    }

    private Dictionary<string, CurrencyLayout> Load()
    {
        if (_layouts is not null)
        {
            return _layouts;
        }

        if (!File.Exists(ConfigurationPath))
        {
            return _layouts = new Dictionary<string, CurrencyLayout>(StringComparer.OrdinalIgnoreCase);
        }

        _layouts = ResilientJsonFile.ReadOrBackupCorrupted<Dictionary<string, CurrencyLayout>>(
            ConfigurationPath,
            JsonOptions,
            layouts => layouts.All(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) &&
                pair.Value is not null &&
                !string.IsNullOrWhiteSpace(pair.Value.RegionName) &&
                pair.Value.Slots is not null))
            ?? new Dictionary<string, CurrencyLayout>(StringComparer.OrdinalIgnoreCase);
        return _layouts;
    }

    private void Save(IReadOnlyDictionary<string, CurrencyLayout> layouts) =>
        ResilientJsonFile.WriteAtomically(ConfigurationPath, layouts, JsonOptions);
}
