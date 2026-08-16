using System.Windows.Forms;

namespace Poe2DeskTracker.Interop;

internal static class WindowsFormsRuntime
{
    private static int _highDpiConfigured;

    internal static void EnsureHighDpiMode()
    {
        if (Interlocked.Exchange(ref _highDpiConfigured, 1) == 0)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
    }
}
