namespace Poe2DesktopClock.Application.Models;

/// <summary>User-facing result for one configured public-tab marker.</summary>
public sealed record PublicTabSynchronizationResult(
    PublicTabsSetupTab Tab,
    PublicTabSynchronizationStatus Status,
    string RussianSummary)
{
    public bool IsSynchronized => Status == PublicTabSynchronizationStatus.Synchronized;
}
