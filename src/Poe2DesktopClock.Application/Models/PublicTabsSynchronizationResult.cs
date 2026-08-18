namespace Poe2DesktopClock.Application.Models;

/// <summary>Complete result of one public-tabs setup synchronization attempt.</summary>
public sealed record PublicTabsSynchronizationResult(
    string AccountName,
    string League,
    IReadOnlyList<PublicTabSynchronizationResult> Tabs)
{
    public int SelectedCount => Tabs.Count(tab => tab.Tab.IsSelected);

    public int SynchronizedCount => Tabs.Count(tab => tab.Tab.IsSelected && tab.IsSynchronized);

    public bool AreAllSelectedTabsSynchronized =>
        SelectedCount > 0 && SynchronizedCount == SelectedCount;

    public string RussianSummary => $"Синхронизировано {SynchronizedCount} из {SelectedCount}.";
}
