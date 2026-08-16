namespace Poe2DeskTracker.Regions;

public sealed record RegionDefinition(
    string Name,
    double X,
    double Y,
    double Width,
    double Height,
    int ReferenceWidth,
    int ReferenceHeight);
