using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Verifies the selected public stash tabs before they become part of the
/// tracker configuration. This is intentionally separate from valuation and
/// background refresh use cases.
/// </summary>
public interface IPublicTabsSetupUseCase
{
    /// <summary>
    /// Returns the fixed marker catalog, applying persisted selection when a
    /// valid public-tab configuration already exists.
    /// </summary>
    IReadOnlyList<PublicTabsSetupTab> GetTabs();

    /// <summary>
    /// Returns whether a complete public-tab configuration was previously
    /// persisted. This is distinct from the default-selected catalog returned
    /// by <see cref="GetTabs"/> and supports first-run migration decisions.
    /// </summary>
    bool HasSavedConfiguration();

    /// <summary>
    /// Checks each selected marker sequentially and returns an outcome for
    /// every row, including excluded rows. Cancellation stops the operation
    /// without persisting any configuration.
    /// </summary>
    Task<PublicTabsSynchronizationResult> SynchronizeAsync(
        PublicTabsSetupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists only selected rows that were successfully synchronized in the
    /// supplied result. Callers can therefore display failed rows without
    /// accidentally enabling them for background tracking.
    /// </summary>
    Task SaveAsync(
        PublicTabsSynchronizationResult synchronization,
        CancellationToken cancellationToken = default);
}
