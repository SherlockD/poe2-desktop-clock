using System.Text.Json;
using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Infrastructure.Storage.Persistence;

namespace Poe2DesktopClock.Infrastructure.Storage.Onboarding;

/// <summary>
/// JSON-backed durable state for the one-time initial setup flow.
/// </summary>
public sealed class InitialSetupStateStore : IInitialSetupStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private InitialSetupState? _state;
    private bool _loaded;

    public InitialSetupStateStore(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ConfigurationPath = configurationPath;
    }

    public string ConfigurationPath { get; }

    public InitialSetupState Get()
    {
        lock (_sync)
        {
            Load();
            return _state!;
        }
    }

    public void Save(InitialSetupState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsValid(state))
        {
            throw new ArgumentException("Состояние первоначальной настройки содержит некорректные данные.", nameof(state));
        }

        lock (_sync)
        {
            ResilientJsonFile.WriteAtomically(ConfigurationPath, state, JsonOptions);
            _state = state;
            _loaded = true;
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _state = ResilientJsonFile.ReadOrBackupCorrupted<InitialSetupState>(
            ConfigurationPath,
            JsonOptions,
            IsValid) ?? InitialSetupState.NotStarted;
        _loaded = true;
    }

    private static bool IsValid(InitialSetupState state) =>
        state.SchemaVersion == InitialSetupState.CurrentSchemaVersion &&
        state.CompletedVersion is >= 0 and <= InitialSetupState.CurrentSetupVersion &&
        Enum.IsDefined(state.LastVisitedStep);
}
