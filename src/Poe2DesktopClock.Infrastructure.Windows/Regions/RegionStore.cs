using System.Text.Json;
using Poe2DesktopClock.Infrastructure.Windows.Persistence;

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

            Save(regions);
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
        ResilientJsonFile.WriteAtomically(ConfigurationPath, regions, JsonOptions);
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

        _regions = ResilientJsonFile.ReadOrBackupCorrupted<List<RegionDefinition>>(
            sourcePath,
            JsonOptions,
            regions => regions.All(region => region is not null && !string.IsNullOrWhiteSpace(region.Name))) ?? [];
        if (!string.Equals(sourcePath, ConfigurationPath, StringComparison.OrdinalIgnoreCase))
        {
            Save(_regions);
        }

        return _regions;
    }
}
