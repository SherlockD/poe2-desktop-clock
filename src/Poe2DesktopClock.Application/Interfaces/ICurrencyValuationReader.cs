using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ICurrencyValuationReader
{
    Task<CurrencyValuation?> ReadAsync(
        CurrencyTabFrame frame,
        PriceSnapshot? prices,
        CancellationToken cancellationToken = default);
}
