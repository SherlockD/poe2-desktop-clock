namespace Poe2DesktopClock.Application.Interfaces;

public interface ISystemTrayIcon : IDisposable
{
    event EventHandler? RestoreRequested;

    event EventHandler? ExitRequested;

    void Show();

    void Hide();
}
