using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ICurrencyValuationReader
{
    Task<CurrencyValuation?> ReadAsync(PriceSnapshot? prices, CancellationToken cancellationToken = default);
}
