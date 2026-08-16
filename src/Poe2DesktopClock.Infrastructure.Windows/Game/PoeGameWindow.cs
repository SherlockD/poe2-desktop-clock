namespace Poe2DeskTracker.Game;

public sealed record PoeGameWindow(nint Handle, int ProcessId, string Title, int Width, int Height);
