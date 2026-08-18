using System.Text.Json;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.Persistence;

namespace Poe2DesktopClock.Infrastructure.Storage.Settings;

/// <summary>
/// Хранит только настройки desktop-приложения. Данные существующего трекера
/// остаются в прежней папке, поэтому первая desktop-версия не теряет калибровку.
/// </summary>
public sealed class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private TrackerSettings? _settings;

    public DesktopSettingsStore(string configurationPath)
    {
        ConfigurationPath = configurationPath;
    }

    public string ConfigurationPath { get; }

    public TrackerSettings Get(TrackerSettings fallback)
    {
        lock (_sync)
        {
            if (_settings is not null)
            {
                return _settings;
            }

            _settings = ResilientJsonFile.ReadOrBackupCorrupted<TrackerSettings>(ConfigurationPath, JsonOptions)?.Normalize()
                ?? fallback.Normalize();
            return _settings;
        }
    }

    public void Save(TrackerSettings settings)
    {
        lock (_sync)
        {
            _settings = settings.Normalize();
            ResilientJsonFile.WriteAtomically(ConfigurationPath, _settings, JsonOptions);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _settings = null;
            if (File.Exists(ConfigurationPath))
            {
                File.Delete(ConfigurationPath);
            }
        }
    }
}
