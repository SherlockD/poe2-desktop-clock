namespace Poe2DesktopClock.Application.Models;

/// <summary>Configured public tab identity, independent of storage format.</summary>
public sealed record PublicTabMarker(string Label, string TabName, decimal PriceAmount, string PriceCurrency);
