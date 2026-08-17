using Poe2DesktopClock.Contracts.Models;

namespace Poe2DesktopClock.Application.Interfaces;

public interface IGameStatusReader
{
    GameStatus GetGameStatus();
}
