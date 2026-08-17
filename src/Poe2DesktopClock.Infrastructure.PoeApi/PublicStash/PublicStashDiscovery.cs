namespace Poe2DeskTracker.PublicStash;

public sealed record PublicStashDiscovery(
    IReadOnlyList<PublicStashSearchGroupResult> SearchGroups,
    int ReturnedUniqueItemIds,
    IReadOnlyList<PublicStashItem> Items)
{
    public bool IsTruncated => SearchGroups.Any(group => group.IsTruncated);
}
