using Poe2DesktopClock.Contracts.Models;
using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Desktop.Models;

/// <summary>
/// Raw monitoring state assembled for the WPF dashboard. Presentation text is
/// created in the view model so the application contracts stay transport- and
/// UI-neutral.
/// </summary>
public sealed record TrackerStatusSnapshot(
    ClockSnapshot? ClockSnapshot,
    PublicTabsValuation? PublicTabsValuation,
    GameStatus GameStatus,
    ClockMonitorStatus MonitorStatus,
    GameSessionSnapshot Session,
    DeviceSynchronizationState Device);
