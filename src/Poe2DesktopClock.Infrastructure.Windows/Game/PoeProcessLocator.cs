using System.Diagnostics;
using System.Text.RegularExpressions;
using Poe2DeskTracker.Interop;

namespace Poe2DeskTracker.Game;

public sealed partial class PoeProcessLocator
{
    private static readonly string[] KnownProcessNames = ["PathOfExile", "PathOfExile_x64", "PathOfExileSteam", "PathOfExile2"];

    /// <summary>
    /// Finds the PoE 2 process even when its window is minimized. The process
    /// identity is used to delimit a tracking session; capture still requires
    /// a visible game window.
    /// </summary>
    public PoeGameProcess? FindGameProcess()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!IsCandidate(process))
                {
                    continue;
                }

                try
                {
                    return new PoeGameProcess(
                        process.Id,
                        new DateTimeOffset(process.StartTime.ToUniversalTime()));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while it was being inspected.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Process metadata can be unavailable for a short time.
                }
            }
        }

        return null;
    }

    public PoeGameWindow? FindGameWindow(bool includeMinimized = false)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!IsCandidate(process))
                {
                    continue;
                }

                var handle = process.MainWindowHandle;
                if (handle == nint.Zero ||
                    !Win32Native.IsWindowVisible(handle) ||
                    (!includeMinimized && Win32Native.IsIconic(handle)))
                {
                    continue;
                }

                if (!Win32Native.TryGetClientSize(handle, out var width, out var height) || width <= 0 || height <= 0)
                {
                    continue;
                }

                return new PoeGameWindow(handle, process.Id, process.MainWindowTitle, width, height);
            }
        }

        return null;
    }

    private static bool IsCandidate(Process process)
    {
        try
        {
            return KnownProcessNames.Any(name =>
                       string.Equals(name, process.ProcessName, StringComparison.OrdinalIgnoreCase)) &&
                   PathOfExileTwoTitle().IsMatch(process.MainWindowTitle);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"path\s+of\s+exile\s*2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PathOfExileTwoTitle();
}
