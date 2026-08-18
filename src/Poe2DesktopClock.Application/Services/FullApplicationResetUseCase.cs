using Poe2DesktopClock.Application.Interfaces;

namespace Poe2DesktopClock.Application.Services;

/// <summary>
/// Coordinates the explicit user-requested return to a clean first-run state.
/// </summary>
public sealed class FullApplicationResetUseCase : IFullApplicationResetUseCase
{
    private readonly IApplicationDataResetter _dataResetter;

    public FullApplicationResetUseCase(IApplicationDataResetter dataResetter) =>
        _dataResetter = dataResetter ?? throw new ArgumentNullException(nameof(dataResetter));

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dataResetter.Reset();
        return Task.CompletedTask;
    }
}
