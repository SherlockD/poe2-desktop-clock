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
        var gameWindow = _processLocator.FindGameWindow();
        return gameWindow is null
            ? new GameStatus(false, "Path of Exile 2 не найден или свёрнут.")
            : new GameStatus(true, $"Path of Exile 2 найден: {gameWindow.Width}×{gameWindow.Height}.", gameWindow.ProcessId, gameWindow.Width, gameWindow.Height);
    }
}
