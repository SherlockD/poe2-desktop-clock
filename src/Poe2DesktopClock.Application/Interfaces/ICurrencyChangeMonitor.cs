using Poe2DesktopClock.Application.Models;
using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Platform adapter that observes the Currency tab. It does not own application
/// refresh state, scheduling, or subscribers.
/// </summary>
public interface ICurrencyChangeMonitor
{
    event EventHandler<CurrencyTabChangedEventArgs>? CurrencyChanged;

    event EventHandler<ClockMonitorStatus>? StatusChanged;

    Task RunAsync(TimeSpan pollingPeriod, CancellationToken cancellationToken);
}
