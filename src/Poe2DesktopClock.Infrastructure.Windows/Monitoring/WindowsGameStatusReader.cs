using Poe2DesktopClock.Application.Interfaces;
using Poe2DesktopClock.Contracts.Models;
using Poe2DeskTracker.Game;

namespace Poe2DesktopClock.Infrastructure.Windows.Monitoring;

public sealed class WindowsGameStatusReader : IGameStatusReader
{
    private readonly PoeProcessLocator _processLocator;

    public WindowsGameStatusReader(PoeProcessLocator processLocator) => _processLocator = processLocator;

    public GameStatus GetGameStatus()
    {
        var process = _processLocator.FindGameProcess();
        if (process is null)
        {
            return new GameStatus(false, "Path of Exile 2 не запущен.");
        }

        var gameWindow = _processLocator.FindGameWindow();
        return gameWindow is null
            ? new GameStatus(
                true,
                "Path of Exile 2 запущен, но окно недоступно для захвата.",
                process.ProcessId,
                ProcessStartedAt: process.StartedAt)
            : new GameStatus(
                true,
                $"Path of Exile 2 найден: {gameWindow.Width}×{gameWindow.Height}.",
                gameWindow.ProcessId,
                gameWindow.Width,
                gameWindow.Height,
                process.StartedAt);
    }
}
