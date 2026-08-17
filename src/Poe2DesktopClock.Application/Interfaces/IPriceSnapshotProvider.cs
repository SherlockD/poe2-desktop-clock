using Poe2DesktopClock.Application.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface IPriceSnapshotProvider
{
    Task<PriceSnapshot?> GetAsync(
        string league,
        TimeSpan maximumAge,
        CancellationToken cancellationToken = default);
}
