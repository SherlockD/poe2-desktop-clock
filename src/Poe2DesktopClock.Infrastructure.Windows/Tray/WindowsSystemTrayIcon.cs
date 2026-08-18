using System.Drawing;
using System.Windows.Forms;
using Poe2DesktopClock.Application.Interfaces;

namespace Poe2DesktopClock.Infrastructure.Windows.Tray;

public sealed class WindowsSystemTrayIcon : ISystemTrayIcon
{
    private readonly ContextMenuStrip _contextMenu;
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public WindowsSystemTrayIcon()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Открыть", null, OnRestoreClicked);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Выход", null, OnExitClicked);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = SystemIcons.Application,
            Text = "PoE 2 Desktop Clock",
            Visible = false,
        };
        _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
    }

    public event EventHandler? RestoreRequested;

    public event EventHandler? ExitRequested;

    public void Show()
    {
        ThrowIfDisposed();
        _notifyIcon.Visible = true;
    }

    public void Hide()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }

    private void OnNotifyIconDoubleClick(object? sender, EventArgs eventArgs) =>
        RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreClicked(object? sender, EventArgs eventArgs) =>
        RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClicked(object? sender, EventArgs eventArgs) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
