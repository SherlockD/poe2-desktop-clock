using System.Text.Json;

namespace Poe2DeskTracker.Regions;

public sealed class RegionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly IReadOnlyList<string> _legacyConfigurationPaths;
    private List<RegionDefinition>? _regions;

    public RegionStore(string configurationPath, params string[] legacyConfigurationPaths)
    {
        ConfigurationPath = configurationPath;
        _legacyConfigurationPaths = legacyConfigurationPaths;
    }

    public string ConfigurationPath { get; }

    public IReadOnlyList<RegionDefinition> GetAll()
    {
        lock (_sync)
        {
            return Load().OrderBy(region => region.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public void Upsert(RegionDefinition region)
    {
        lock (_sync)
        {
            var regions = Load();
            var existingIndex = regions.FindIndex(existing => string.Equals(existing.Name, region.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                regions[existingIndex] = region;
            }
            else
            {
                regions.Add(region);
            }

            var directory = Path.GetDirectoryName(ConfigurationPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(regions, JsonOptions));
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _regions = [];
            Save(_regions);
        }
    }

    private void Save(IReadOnlyList<RegionDefinition> regions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
        File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(regions, JsonOptions));
    }

    private List<RegionDefinition> Load()
    {
        if (_regions is not null)
        {
            return _regions;
        }

        var sourcePath = File.Exists(ConfigurationPath)
            ? ConfigurationPath
            : _legacyConfigurationPaths.FirstOrDefault(File.Exists);
        if (sourcePath is null)
        {
            return _regions = [];
        }

        _regions = JsonSerializer.Deserialize<List<RegionDefinition>>(File.ReadAllText(sourcePath), JsonOptions) ?? [];
        if (!string.Equals(sourcePath, ConfigurationPath, StringComparison.OrdinalIgnoreCase))
        {
            Save(_regions);
        }

        return _regions;
    }
}
