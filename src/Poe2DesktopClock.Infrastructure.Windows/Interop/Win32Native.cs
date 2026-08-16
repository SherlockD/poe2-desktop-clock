using System.Runtime.InteropServices;

namespace Poe2DeskTracker.Interop;

internal static partial class Win32Native
{
    private const int SwRestore = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    internal static void RestoreAndActivateWindow(nint hWnd)
    {
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SwRestore);
        }

        SetForegroundWindow(hWnd);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out Rect rectangle);

    internal static bool TryGetClientSize(nint hWnd, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!GetClientRect(hWnd, out var rectangle))
        {
            return false;
        }

        width = rectangle.Right - rectangle.Left;
        height = rectangle.Bottom - rectangle.Top;
        return true;
    }

    internal static bool TryGetClientBoundsOnScreen(nint hWnd, out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;

        if (!GetClientRect(hWnd, out var clientRectangle))
        {
            return false;
        }

        var upperLeft = new Point(clientRectangle.Left, clientRectangle.Top);
        var lowerRight = new Point(clientRectangle.Right, clientRectangle.Bottom);
        if (!ClientToScreen(hWnd, ref upperLeft) || !ClientToScreen(hWnd, ref lowerRight))
        {
            return false;
        }

        left = upperLeft.X;
        top = upperLeft.Y;
        width = lowerRight.X - upperLeft.X;
        height = lowerRight.Y - upperLeft.Y;
        return width > 0 && height > 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref Point point);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect
    {
        internal readonly int Left;
        internal readonly int Top;
        internal readonly int Right;
        internal readonly int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        internal int X = x;
        internal int Y = y;
    }
}
