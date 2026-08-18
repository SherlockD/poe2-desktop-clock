namespace Poe2DesktopClock.Application.Models;

/// <summary>Outcome of checking one public-tab marker during setup.</summary>
public enum PublicTabSynchronizationStatus
{
    Synchronized,
    NotFound,
    WrongTabName,
    Ambiguous,
    Error,
    Excluded,
}
