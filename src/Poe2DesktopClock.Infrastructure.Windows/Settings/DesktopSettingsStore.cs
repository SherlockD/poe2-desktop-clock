using System.Text.Json;
using Poe2DesktopClock.Core.Models;

namespace Poe2DesktopClock.Infrastructure.Windows.Settings;

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

            if (!File.Exists(ConfigurationPath))
            {
                return _settings = fallback.Normalize();
            }

            _settings = JsonSerializer.Deserialize<TrackerSettings>(File.ReadAllText(ConfigurationPath), JsonOptions)?.Normalize()
                ?? fallback.Normalize();
            return _settings;
        }
    }

    public void Save(TrackerSettings settings)
    {
        lock (_sync)
        {
            _settings = settings.Normalize();
            var directory = Path.GetDirectoryName(ConfigurationPath)
                ?? throw new InvalidOperationException("Не удалось определить папку настроек.");
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{ConfigurationPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_settings, JsonOptions));
            File.Move(temporaryPath, ConfigurationPath, overwrite: true);
        }
    }
}
