namespace Poe2DeskTracker.PublicStash;

public sealed record PublicStashFetchProgress(
    int CurrentBatch,
    int TotalBatches,
    int ItemCount);
