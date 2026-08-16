using Poe2DeskTracker.Currency;
using Poe2DeskTracker.Regions;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

internal sealed record CurrencyPrerequisites(RegionDefinition Region, CurrencyLayout Layout);
