using System.Text.Json;

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
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(layouts, JsonOptions));
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _layouts = new Dictionary<string, CurrencyLayout>(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(_layouts, JsonOptions));
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

        _layouts = JsonSerializer.Deserialize<Dictionary<string, CurrencyLayout>>(File.ReadAllText(ConfigurationPath), JsonOptions)
            ?? new Dictionary<string, CurrencyLayout>(StringComparer.OrdinalIgnoreCase);
        return _layouts;
    }
}
