namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// The durable checkpoints of the one-time initial setup flow.
/// </summary>
public enum InitialSetupStep
{
    CurrencyTab = 1,
    PublicTabs = 2,
    DeviceConnection = 3,
}
