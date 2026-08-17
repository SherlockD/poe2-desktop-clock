namespace Poe2DesktopClock.Application.Interfaces;

public interface ILeagueCatalog
{
    Task<IReadOnlyList<string>> GetPoe2LeaguesAsync(CancellationToken cancellationToken = default);
}
