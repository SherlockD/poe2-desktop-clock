namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Erases the user's persisted tracker configuration and returns the application
/// to its first-run state.
/// </summary>
public interface IFullApplicationResetUseCase
{
    Task ResetAsync(CancellationToken cancellationToken = default);
}
