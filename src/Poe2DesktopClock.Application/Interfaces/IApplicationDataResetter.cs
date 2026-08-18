namespace Poe2DesktopClock.Application.Interfaces;

/// <summary>
/// Infrastructure boundary for clearing every persisted store owned by the
/// desktop tracker.
/// </summary>
public interface IApplicationDataResetter
{
    void Reset();
}
