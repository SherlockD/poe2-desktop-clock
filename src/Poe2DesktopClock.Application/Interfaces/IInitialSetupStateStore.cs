using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Reads and persists durable progress of the one-time initial setup flow.
/// </summary>
public interface IInitialSetupStateStore
{
    InitialSetupState Get();

    void Save(InitialSetupState state);
}
