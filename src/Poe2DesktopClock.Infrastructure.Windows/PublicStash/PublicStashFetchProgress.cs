namespace Poe2DeskTracker.PublicStash;

internal sealed record PublicStashFetchProgress(
    int CurrentBatch,
    int TotalBatches,
    int ItemCount);
