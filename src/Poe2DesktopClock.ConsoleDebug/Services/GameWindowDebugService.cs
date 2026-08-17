using Poe2DeskTracker.Capture;
using Poe2DeskTracker.Game;

namespace Poe2DesktopClock.ConsoleDebug.Services;

internal sealed class GameWindowDebugService
{
    private readonly PoeProcessLocator _locator;
    private readonly WindowsGraphicsCaptureService _capture;

    public GameWindowDebugService(PoeProcessLocator locator, WindowsGraphicsCaptureService capture)
    {
        _locator = locator;
        _capture = capture;
    }

    public void PrintStatus()
    {
        var gameWindow = _locator.FindGameWindow();
        if (gameWindow is null)
        {
            Console.WriteLine("PoE 2: NOT FOUND");
            Console.WriteLine("Waiting...");
            return;
        }

        Console.WriteLine($"PoE 2: FOUND (PID {gameWindow.ProcessId}, {gameWindow.Title})");
        Console.WriteLine($"Window: {gameWindow.Width}x{gameWindow.Height}");
        Console.WriteLine("Capture: READY");
    }

    public async Task SaveFrameAsync()
    {
        var gameWindow = _locator.FindGameWindow();
        if (gameWindow is null)
        {
            Console.WriteLine("PoE 2: NOT FOUND — start the game and try again.");
            return;
        }

        var outputPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "debug", "frame.png"));
        try
        {
            Console.WriteLine($"Capturing PID {gameWindow.ProcessId}...");
            var result = await _capture.SaveSingleFrameAsync(gameWindow.Handle, outputPath, TimeSpan.FromSeconds(5));
            Console.WriteLine($"Capture: ACTIVE ({result.Width}x{result.Height}, {result.Elapsed.TotalMilliseconds:F0} ms)");
            Console.WriteLine($"Debug frame: {outputPath}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Capture timed out. Ensure PoE 2 is visible and not minimized.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Capture failed: {exception.Message}");
        }
    }
}
