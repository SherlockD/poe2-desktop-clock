using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ICurrencyRefreshUseCase
{
    event EventHandler<CurrencyRefreshResult>? Refreshed;

    Task<CurrencyRefreshResult?> RefreshAsync(
        CurrencyTabFrame frame,
        CancellationToken cancellationToken = default);
}
