namespace Poe2DeskTracker.PublicStash;

internal sealed record PublicStashDiscovery(
    IReadOnlyList<PublicStashSearchGroupResult> SearchGroups,
    int ReturnedUniqueItemIds,
    IReadOnlyList<PublicStashItem> Items)
{
    internal bool IsTruncated => SearchGroups.Any(group => group.IsTruncated);
}
