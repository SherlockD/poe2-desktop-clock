using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface IClockSnapshotComposer
{
    ClockSnapshot Compose(
        CurrencyValuation? currency,
        PublicTabsValuation? publicTabs,
        DateTimeOffset? pricesUpdatedAt);
}
