using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ITrackerMonitoringUseCase
{
    event EventHandler<ClockMonitorStatus>? MonitorStatusChanged;

    GameStatus GetGameStatus();

    Task StartCurrencyMonitoringAsync(CancellationToken cancellationToken = default);

    Task StopCurrencyMonitoringAsync();
}
