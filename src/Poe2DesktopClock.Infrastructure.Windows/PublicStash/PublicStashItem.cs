namespace Poe2DeskTracker.PublicStash;

internal sealed record PublicStashItem(
    string? Id,
    string TabName,
    string ItemName,
    long StackSize,
    int? X,
    int? Y,
    string MarkerLabel);
