using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface ICurrencySetupUseCase
{
    CurrencySetupStatus GetCurrencySetupStatus();

    Task SelectCurrencyRegionAsync(CancellationToken cancellationToken = default);

    Task CalibrateCurrencySlotsAsync(CancellationToken cancellationToken = default);
}
