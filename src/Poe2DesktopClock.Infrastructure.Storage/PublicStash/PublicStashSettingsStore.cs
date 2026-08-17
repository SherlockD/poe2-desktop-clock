using System.Text.Json;
using Poe2DeskTracker.PublicStash;

namespace Poe2DesktopClock.Infrastructure.Storage.PublicStash;

public sealed class PublicStashSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private PublicStashSettings? _settings;
    private bool _loaded;

    public PublicStashSettingsStore(string configurationPath)
    {
        ConfigurationPath = configurationPath;
    }

    public string ConfigurationPath { get; }

    public PublicStashSettings? Get()
    {
        lock (_sync)
        {
            Load();
            return _settings is null
                ? null
                : _settings with
                {
                    TabNames = [.. _settings.TabNames],
                    TabMarkers = _settings.TabMarkers?.Select(marker => marker with { }).ToList(),
                };
        }
    }

    public void Save(PublicStashSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.AccountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.League);

        var markers = (settings.TabMarkers ?? [])
            .Select(marker => new PublicStashTabMarker(
                marker.Label.Trim(),
                marker.TabName.Trim(),
                marker.PriceAmount,
                marker.PriceCurrency.Trim().ToLowerInvariant()))
            .Where(marker => !string.IsNullOrWhiteSpace(marker.Label) &&
                             !string.IsNullOrWhiteSpace(marker.TabName) &&
                             marker.PriceAmount > 0 &&
                             !string.IsNullOrWhiteSpace(marker.PriceCurrency))
            .ToList();
        if (markers.Count == 0)
        {
            throw new ArgumentException("Configure at least one public stash tab marker.", nameof(settings));
        }

        if (markers.Select(marker => marker.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != markers.Count ||
            markers.Select(marker => marker.TabName).Distinct(StringComparer.Ordinal).Count() != markers.Count ||
            markers.Select(marker => $"{marker.PriceAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}\u001f{marker.PriceCurrency}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != markers.Count)
        {
            throw new ArgumentException("Public-tab labels, names, and marker prices must each be unique.", nameof(settings));
        }

        lock (_sync)
        {
            _settings = new PublicStashSettings(
                settings.AccountName.Trim(),
                settings.League.Trim(),
                markers.Select(marker => marker.TabName).ToList(),
                markers);
            _loaded = true;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(_settings, JsonOptions));
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _settings = null;
            _loaded = true;
            if (File.Exists(ConfigurationPath))
            {
                File.Delete(ConfigurationPath);
            }
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(ConfigurationPath))
        {
            return;
        }

        _settings = JsonSerializer.Deserialize<PublicStashSettings>(File.ReadAllText(ConfigurationPath), JsonOptions);
    }
}
