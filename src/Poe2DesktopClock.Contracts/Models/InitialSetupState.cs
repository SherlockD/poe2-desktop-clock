namespace Poe2DesktopClock.Contracts.Models;

/// <summary>
/// Durable metadata for the one-time initial setup flow. Source settings such
/// as currency calibration and public-tab markers remain in their own stores.
/// </summary>
public sealed record InitialSetupState(
    int SchemaVersion,
    int CompletedVersion,
    InitialSetupStep LastVisitedStep)
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentSetupVersion = 1;

    public static InitialSetupState NotStarted { get; } = new(
        CurrentSchemaVersion,
        CompletedVersion: 0,
        InitialSetupStep.CurrencyTab);

    public bool IsCompleted => CompletedVersion >= CurrentSetupVersion;
}
